using System.Text.Json.Nodes;
using Connector.Core.DataSources;
using Connector.Core.DynamicExport;
using Connector.Infrastructure.DataSources;

namespace Connector.Infrastructure;

// ── ExportNode tree engine (Phase 14) ───────────────────────────────────────
// Generalizes the legacy nested-JSON path (DynamicExportService.LegacyMapping.cs's BuildNestedGroupPlan/
// ExecuteNestedJsonQueryAsync) to a single recursive tree walk that also emits plain scalar-field columns,
// so one query shape serves every output format instead of the legacy flat-vs-nested-JSON fork. There is
// deliberately no ExportNode counterpart to UsesNestedJson: every ExportNode tree is queried the same way
// regardless of format (see BuildExportNodeAsync) — only the format WRITER differs (IExportFormatWriter in
// ExportFormatWriters.cs), which is the actual OCP seam knowledge/pipeline/export-definitions-2.0.md §8 asks for.
// The database only returns relational rows; the nested records are assembled in C# by the tree query
// engine (DynamicExportService.TreeQuery.cs).
public static partial class DynamicExportService
{
    /// <summary>
    /// Compiles one <see cref="ExportNode"/>'s enabled children into a <see cref="TreePlan"/> for the tree
    /// query engine: <see cref="ExportNodeKind.ScalarField"/> children become text-valued columns, every other
    /// child a nested object (<see cref="ExportNodeKind.Object"/>) or array (<see cref="ExportNodeKind.Array"/>)
    /// joined on its <see cref="ExportNode.JoinKey"/>/<see cref="ExportNode.SourceJoinKey"/> and scoped by its
    /// own <see cref="ExportNode.Filter"/>. Fully built — including the depth check — before any query runs.
    /// </summary>
    private static TreePlan BuildExportNodePlan(
        string table,
        string alias,
        ExportNode node,
        ref int aliasCounter,
        int depth,
        IReadOnlySet<string> denylist
    )
    {
        var members = new List<TreeMember>();
        foreach (var child in node.Children.Where(x => x.Enabled))
        {
            if (child.Kind == ExportNodeKind.ScalarField)
            {
                // GDPR denylist check keyed on SourceField (the actual column), not TargetKey (the export's
                // chosen output name) — security-review finding SR-08: a field renamed away from its source
                // name must still be excluded, including a definition saved before this field was
                // denylisted, since this filter re-applies fresh against the *current* denylist on every
                // run. Excluding it from the SELECT list entirely (rather than relying only on
                // StripGdprFieldsRecursive's output-key match) means the value never leaves the database.
                if (!denylist.Contains(child.SourceField!))
                    members.Add(new TreeScalar(child.TargetKey, child.SourceField!));
                continue;
            }

            if (depth > MaxNestedDepth)
                throw new InvalidOperationException(
                    $"Export node '{child.TargetKey}' exceeds the maximum nesting depth of {MaxNestedDepth}."
                );

            // Synthetic alias, not derived from RelatedTable/TargetKey, numbered in pre-order exactly as the
            // pre-Arbeitsauftrag-6 correlated subqueries were — so a stored Filter that names it keeps working.
            var childAlias = $"en{aliasCounter++}";
            var childPlan = BuildExportNodePlan(
                child.RelatedTable!,
                childAlias,
                child,
                ref aliasCounter,
                depth + 1,
                denylist
            );
            members.Add(
                new TreeChild(
                    child.TargetKey,
                    IsArray: child.Kind == ExportNodeKind.Array,
                    child.JoinKey!,
                    child.SourceJoinKey!,
                    childPlan,
                    ObjectNodeCardinalityErrorMessage
                )
            );
        }

        return new TreePlan(table, alias, node.Filter, members, CastScalarsToText: true);
    }

    /// <summary>
    /// Runs one <see cref="ExportNode"/> tree (rooted at <paramref name="rootTable"/>) and returns one JSON tree
    /// per root row — the generic successor to <see cref="ExecuteNestedJsonQueryAsync"/> that also covers plain
    /// scalar columns, so it is the only query path <see cref="BuildExportNodeAsync"/> needs regardless of
    /// output format. The database returns plain rows, one query per tree node; the tree is assembled in C#
    /// (<see cref="ExecuteTreeQueryAsync"/>). Every scalar is a JSON string of its <c>::text</c> form (or JSON
    /// null), an array node with no matches is <c>[]</c>, an object node with no match is <c>null</c>, and an
    /// object node with several matches fails the export. GDPR enforcement is two layers: a denylisted <see
    /// cref="ExportNode.SourceField"/> is excluded from the SELECT list entirely (so it never leaves the
    /// database, and — since this is re-evaluated against the *current* denylist on every run — a
    /// definition saved before a field was denylisted is covered too, not just newly-saved ones), plus
    /// <see cref="StripGdprFieldsRecursive"/>'s output-key match as a second, independent layer of
    /// defence-in-depth (security-review finding SR-08).
    /// </summary>
    public static async Task<List<JsonObject>> ExecuteExportNodeQueryAsync(
        IDataSourceProvider provider,
        DataSourceConfig dsConfig,
        string rootTable,
        ExportNode root,
        CancellationToken ct,
        int? limit = null,
        IReadOnlySet<string>? gdprDenylist = null
    )
    {
        var effectiveDenylist = gdprDenylist ?? GdprDeniedFields;

        var aliasCounter = 0;
        var plan = BuildExportNodePlan(rootTable, "s", root, ref aliasCounter, depth: 1, effectiveDenylist);

        // An explicit caller limit (e.g. a preview) is honored exactly; otherwise the query is still capped
        // at MaxExportRowsPerRun + 1 server-side so a runaway/unfiltered definition can't read an unbounded
        // result set — the "+1" is how the post-read count below tells "exactly at the cap" apart from
        // "more rows exist" without a separate COUNT(*) query.
        var sqlLimit = Math.Min(limit ?? int.MaxValue, MaxExportRowsPerRun + 1);

        var results = await ExecuteTreeQueryAsync(provider, dsConfig, plan, sqlLimit, ct);
        foreach (var node in results)
        {
            StripGdprFieldsRecursive(node, effectiveDenylist);
            ApplyExportNodeMappingsRecursive(node, root);
        }

        // Only the implicit (non-preview) ceiling fails the run — an explicit caller limit is what the
        // caller asked for, not a runaway result, so hitting it exactly is success, not an error.
        if (!limit.HasValue && results.Count > MaxExportRowsPerRun)
            throw new InvalidOperationException(
                $"Export query for '{rootTable}' would return more than {MaxExportRowsPerRun} rows. "
                    + "Narrow the export's filter, or raise DynamicExportService.MaxExportRowsPerRun if this "
                    + "much data is genuinely expected."
            );

        return results;
    }

    /// <summary>See <see cref="ObjectGroupCardinalityErrorMessage"/> — same cardinality guard, for the
    /// <see cref="ExportNode"/> tree engine's own <see cref="ExportNodeKind.Object"/> nodes.</summary>
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
    /// the whole query the way a SQL-side numeric cast failure would. Called by
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
        IDataSourceProvider provider,
        DataSourceConfig dsConfig,
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
        var metered = new MeteredDataSourceProvider(provider);
        var records = await ExecuteExportNodeQueryAsync(metered, dsConfig, rootTable, root, ct, limit, gdprDenylist);
        var writer = ExportFormatWriterFactory.Get(format);
        var bytes = writer.Write(root, records, schemaVersion, extractedAt, provenance);
        return new ExportBuildResult(bytes, records.Count, writer.FileExtension, metered.Metrics);
    }
}
