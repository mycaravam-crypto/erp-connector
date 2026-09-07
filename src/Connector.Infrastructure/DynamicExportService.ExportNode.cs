using System.Text.Json.Nodes;
using Connector.Core.DynamicExport;
using Npgsql;

namespace Connector.Infrastructure;

// ── ExportNode tree engine (Phase 14) ───────────────────────────────────────
// Generalizes the legacy nested-JSON path (DynamicExportService.LegacyMapping.cs's BuildNestedGroupExpr/
// ExecuteNestedJsonQueryAsync) to a single recursive tree walk that also emits plain scalar-field columns,
// so one query shape serves every output format instead of the legacy flat-vs-nested-JSON fork. There is
// deliberately no ExportNode counterpart to UsesNestedJson: every ExportNode tree is queried the same way
// regardless of format (see BuildExportNodeAsync) — only the format WRITER differs (IExportFormatWriter in
// ExportFormatWriters.cs), which is the actual OCP seam knowledge/pipeline/export-definitions-2.0.md §8 asks for.
public static partial class DynamicExportService
{
    /// <summary>
    /// Recursively emits a <c>json_build_object(...)</c> expression for an <see cref="ExportNodeKind.Object"/>
    /// node, or a <c>(SELECT json_agg(...) ...)</c> expression for an <see cref="ExportNodeKind.Array"/> node —
    /// the <see cref="ExportNode"/> counterpart of <see cref="BuildNestedGroupExpr"/>, extended to also emit
    /// <see cref="ExportNodeKind.ScalarField"/> children as plain columns (not just further nesting) and to
    /// scope each node's own <see cref="ExportNode.Filter"/> fragment to its own subquery.
    /// </summary>
    private static string BuildExportNodeExpr(ExportNode node, string parentAlias, ref int aliasCounter, int depth)
    {
        if (depth > MaxNestedDepth)
            throw new InvalidOperationException(
                $"Export node '{node.TargetKey}' exceeds the maximum nesting depth of {MaxNestedDepth}."
            );

        // Synthetic alias, not derived from RelatedTable/TargetKey: avoids collisions when two nodes join
        // the same related table, and is QI-safe by construction (see BuildNestedGroupExpr's alias).
        var alias = $"en{aliasCounter++}";

        var args = new List<string>();
        foreach (var child in node.Children.Where(x => x.Enabled))
        {
            if (child.Kind == ExportNodeKind.ScalarField)
                args.Add($"{SqlLit(child.TargetKey)}, {alias}.{QI(child.SourceField!)}::text");
            else
                args.Add(
                    $"{SqlLit(child.TargetKey)}, {BuildExportNodeExpr(child, alias, ref aliasCounter, depth + 1)}"
                );
        }

        var objectExpr = $"json_build_object({string.Join(", ", args)})";
        // Same COALESCE-only-for-array reasoning as BuildNestedGroupExpr: json_agg() over zero rows
        // returns SQL NULL, not '[]', but a genuinely absent N:1 object node should stay JSON null.
        var agg = node.Kind == ExportNodeKind.Array ? $"COALESCE(json_agg({objectExpr}), '[]'::json)" : objectExpr;

        var filter = string.IsNullOrWhiteSpace(node.Filter) ? "" : $" AND ({node.Filter})";
        return $"(SELECT {agg} FROM {QI(node.RelatedTable!)} {alias} "
            + $"WHERE {alias}.{QI(node.JoinKey!)} = {parentAlias}.{QI(node.SourceJoinKey!)}{filter})";
    }

    /// <summary>
    /// Runs one <see cref="ExportNode"/> tree (rooted at <paramref name="rootTable"/>) as a single query
    /// returning one JSON tree per row — the generic successor to <see cref="ExecuteNestedJsonQueryAsync"/>
    /// that also covers plain scalar columns, so it is the only query path <see cref="BuildExportNodeAsync"/>
    /// needs regardless of output format. GDPR stripping reuses <see cref="StripGdprFieldsRecursive"/>
    /// unchanged since it matches by output key name at every depth, same contract as the legacy path.
    /// </summary>
    public static async Task<List<JsonObject>> ExecuteExportNodeQueryAsync(
        NpgsqlConnection conn,
        string rootTable,
        ExportNode root,
        CancellationToken ct,
        int? limit = null,
        IReadOnlySet<string>? gdprDenylist = null
    )
    {
        var args = new List<string>();
        var aliasCounter = 0;
        foreach (var child in root.Children.Where(x => x.Enabled))
        {
            if (child.Kind == ExportNodeKind.ScalarField)
                args.Add($"{SqlLit(child.TargetKey)}, s.{QI(child.SourceField!)}::text");
            else
                args.Add($"{SqlLit(child.TargetKey)}, {BuildExportNodeExpr(child, "s", ref aliasCounter, depth: 1)}");
        }

        var results = new List<JsonObject>();
        if (args.Count == 0)
            return results;

        var sql = $"SELECT json_build_object({string.Join(", ", args)}) AS row_json FROM {QI(rootTable)} s";
        if (!string.IsNullOrWhiteSpace(root.Filter))
            sql += $" WHERE ({root.Filter})";
        if (limit.HasValue)
            sql += $" LIMIT {limit.Value}";

        var effectiveDenylist = gdprDenylist ?? GdprDeniedFields;

        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 30 };
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var text = await reader.IsDBNullAsync(0, ct) ? "{}" : reader.GetString(0);
                var node = JsonNode.Parse(text) as JsonObject ?? [];
                StripGdprFieldsRecursive(node, effectiveDenylist);
                ApplyExportNodeMappingsRecursive(node, root);
                results.Add(node);
            }
        }
        catch (PostgresException pex) when (pex.SqlState == "21000")
        {
            throw new InvalidOperationException(ObjectNodeCardinalityErrorMessage, pex);
        }

        return results;
    }

    /// <summary>See <see cref="ObjectGroupCardinalityErrorMessage"/> — same cardinality-violation guard,
    /// for the <see cref="ExportNode"/> tree engine's own <see cref="ExportNodeKind.Object"/> nodes.</summary>
    private const string ObjectNodeCardinalityErrorMessage =
        "Export failed: an \"object\" export node matched more than one related row for at least one source "
        + "row. \"object\" nodes assume a 1:1 relationship (via JoinKey/SourceJoinKey) between the source row "
        + "and the related table — if a source row can legitimately match multiple related rows, change that "
        + "node's Kind from \"object\" to \"array\" instead.";

    /// <summary>
    /// Walks a parsed row tree in lockstep with the <see cref="ExportNode"/> tree that produced it, applying
    /// each scalar field's <see cref="FieldMapping"/> (transform/default/data-type coercion) at the point the
    /// value is read — deliberately in C#, not SQL: a malformed single row's value (e.g. non-numeric text
    /// under a <see cref="FieldDataType.Number"/> field) degrades to a best-effort string instead of aborting
    /// the whole query the way a SQL-side <c>::numeric</c> cast failure would. Called by
    /// <see cref="ExecuteExportNodeQueryAsync"/> on every fetched row; public (like this class's other pure
    /// C# post-processing, e.g. <see cref="BuildCsvBytes"/>) so transform behavior is unit-testable against a
    /// hand-built tree without a live Postgres connection.
    /// </summary>
    public static void ApplyExportNodeMappingsRecursive(JsonObject row, ExportNode node)
    {
        foreach (var child in node.Children.Where(x => x.Enabled))
        {
            if (!row.TryGetPropertyValue(child.TargetKey, out var value))
                continue;

            switch (child.Kind)
            {
                case ExportNodeKind.ScalarField:
                    row[child.TargetKey] = ApplyFieldMapping(value, child.Mapping);
                    break;
                case ExportNodeKind.Object when value is JsonObject childObj:
                    ApplyExportNodeMappingsRecursive(childObj, child);
                    break;
                case ExportNodeKind.Array when value is JsonArray arr:
                    foreach (var item in arr.OfType<JsonObject>())
                        ApplyExportNodeMappingsRecursive(item, child);
                    break;
            }
        }
    }

    /// <summary>Internal (not private) so <see cref="ImportNodeWalker"/> can reuse this verbatim for the
    /// write direction (import-definitions.md §5) instead of re-implementing transform/data-type coercion.</summary>
    internal static JsonNode? ApplyFieldMapping(JsonNode? value, FieldMapping? mapping)
    {
        if (mapping is null)
            return value;

        if (mapping.Transform == FieldTransform.Constant)
            return CoerceToDataType(mapping.TransformArg ?? "", mapping.DataType);

        var str = value switch
        {
            null => null,
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            JsonValue v => v.ToJsonString(),
            _ => value.ToJsonString(),
        };

        if (string.IsNullOrEmpty(str))
            return mapping.DefaultValue is null ? null : CoerceToDataType(mapping.DefaultValue, mapping.DataType);

        str = mapping.Transform switch
        {
            FieldTransform.Uppercase => str.ToUpperInvariant(),
            FieldTransform.Lowercase => str.ToLowerInvariant(),
            FieldTransform.Trim => str.Trim(),
            FieldTransform.DateFormat => FormatDateValue(str, mapping.TransformArg),
            _ => str,
        };

        return CoerceToDataType(str, mapping.DataType);
    }

    private static string FormatDateValue(string raw, string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
            return raw;
        return DateTimeOffset.TryParse(
            raw,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var dt
        )
            ? dt.ToString(format, System.Globalization.CultureInfo.InvariantCulture)
            : raw;
    }

    // Best-effort: an unparseable value falls back to its string form rather than throwing, since this
    // runs per-field over already-fetched data (see ApplyExportNodeMappingsRecursive's rationale).
    private static JsonNode? CoerceToDataType(string value, string dataType) =>
        dataType switch
        {
            FieldDataType.Number => decimal.TryParse(
                value,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var num
            )
                ? JsonValue.Create(num)
                : JsonValue.Create(value),
            FieldDataType.Boolean => bool.TryParse(value, out var b) ? JsonValue.Create(b) : JsonValue.Create(value),
            _ => JsonValue.Create(value),
        };

    /// <summary>Dot-path column names for every enabled scalar field reachable in the tree, in tree
    /// order — the CSV/Excel header row for an <see cref="ExportNode"/> tree, since those formats (unlike
    /// JSON) have no native nesting and must flatten to one column per leaf path.</summary>
    public static IReadOnlyList<string> GetExportNodeColumnNames(ExportNode root) =>
        CollectColumnPaths(root, prefix: "").ToList();

    private static IEnumerable<string> CollectColumnPaths(ExportNode node, string prefix)
    {
        foreach (var child in node.Children.Where(x => x.Enabled))
        {
            var path = prefix.Length == 0 ? child.TargetKey : $"{prefix}.{child.TargetKey}";
            if (child.Kind == ExportNodeKind.ScalarField)
                yield return path;
            else
                foreach (var nested in CollectColumnPaths(child, path))
                    yield return nested;
        }
    }

    private const string FlattenJoinDelimiter = ", ";

    /// <summary>
    /// Flattens one <see cref="ExecuteExportNodeQueryAsync"/> row into the same
    /// <c>Dictionary&lt;string,string&gt;</c> shape <see cref="BuildCsvBytes"/>/<see cref="BuildExcelBytes"/>
    /// already consume, so CSV/Excel gain arbitrary-depth nesting (the actual new Phase 14 capability over
    /// the legacy relation-only flattening) without changing either builder. An object path contributes at
    /// most one value; an array path contributes one value per matching row, joined the same way the legacy
    /// <c>string_join</c> relation strategy already did — Phase 14 has no per-node flatten-strategy
    /// equivalent (see knowledge/log.md's Phase 14 Slice 1 entry), so this is the one generic rule for every tree.
    /// </summary>
    public static Dictionary<string, string> FlattenExportNodeRecord(JsonObject row, IReadOnlyList<string> columns)
    {
        var result = new Dictionary<string, string>(columns.Count);
        foreach (var col in columns)
            result[col] = string.Join(FlattenJoinDelimiter, CollectValuesAtPath(row, col.Split('.')));
        return result;
    }

    private static IEnumerable<string> CollectValuesAtPath(JsonNode? node, ReadOnlyMemory<string> segments)
    {
        if (node is null)
            yield break;

        if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                foreach (var v in CollectValuesAtPath(item, segments))
                    yield return v;
            }
            yield break;
        }

        if (segments.Length == 0)
        {
            if (node is JsonValue val)
                yield return val.TryGetValue<string>(out var s) ? s : val.ToJsonString();
            yield break;
        }

        if (node is JsonObject obj && obj.TryGetPropertyValue(segments.Span[0], out var child))
            foreach (var v in CollectValuesAtPath(child, segments[1..]))
                yield return v;
    }

    /// <summary>
    /// Single execution+build path for <see cref="ExportNode"/> trees, the Phase 14 counterpart of
    /// <see cref="BuildExportAsync"/>: one query (<see cref="ExecuteExportNodeQueryAsync"/>) regardless of
    /// format, then dispatches to the requested <see cref="IExportFormatWriter"/>. Unlike the legacy path
    /// there is no per-format query fork to keep in sync — every format writer receives the same tree-shaped
    /// records, which is what makes adding a new format later an OCP-clean addition (knowledge/pipeline/export-definitions-2.0.md §8).
    /// </summary>
    public static async Task<ExportBuildResult> BuildExportNodeAsync(
        NpgsqlConnection conn,
        string rootTable,
        ExportNode root,
        string format,
        string schemaVersion,
        DateTimeOffset extractedAt,
        CancellationToken ct,
        int? limit = null,
        IReadOnlySet<string>? gdprDenylist = null,
        ExportProvenance? provenance = null
    )
    {
        var records = await ExecuteExportNodeQueryAsync(conn, rootTable, root, ct, limit, gdprDenylist);
        var writer = ExportFormatWriterFactory.Get(format);
        var bytes = writer.Write(root, records, schemaVersion, extractedAt, provenance);
        return new ExportBuildResult(bytes, records.Count, writer.FileExtension);
    }
}
