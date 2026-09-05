using System.Text.Json;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using Connector.Core.DynamicExport;
using Connector.Core.Schema;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Connector.Infrastructure;

public static class DynamicExportService
{
    /// <summary>
    /// ERP field names that must never appear in any export artifact (GDPR Art. 5(1)(c)).
    /// Checked at mapping-save time (API) and stripped at query time as defence-in-depth.
    /// This is the hardcoded fallback; admins can override via the gdpr_denied_fields AppSetting.
    /// </summary>
    public static readonly IReadOnlySet<string> GdprDeniedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "technician_name",
        "technician_id",
        "employee_id",
        "contact_name",
        "contact_email",
        "contact_phone",
        "operator_name",
    };

    /// <summary>
    /// Recursion cap for JSON-only nested groups, enforced at save time (the primary, user-facing
    /// rejection point) and again defensively inside <see cref="BuildNestedGroupExpr"/> for any config
    /// that reaches query-build time without going through save-time validation. Far beyond any realistic
    /// use case (item → manufacturer → addresses is depth 2) — this exists solely to turn an unbounded
    /// recursive build into a catchable exception instead of an uncatchable <see cref="StackOverflowException"/>.
    /// </summary>
    public const int MaxNestedDepth = 16;

    /// <summary>
    /// Returns the active GDPR denylist: the DB-stored list if present, else <see cref="GdprDeniedFields"/>.
    /// </summary>
    public static async Task<IReadOnlySet<string>> GetDeniedFieldsAsync(ExportLogDbContext db)
    {
        var setting = await db.AppSettings.FindAsync("gdpr_denied_fields");
        if (setting is not null && !string.IsNullOrWhiteSpace(setting.Value))
        {
            var parsed = JsonSerializer.Deserialize<string[]>(setting.Value);
            if (parsed is { Length: > 0 })
                return new HashSet<string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        return GdprDeniedFields;
    }

    public static IReadOnlyList<string> GetColumnNames(ExportMappingConfig cfg) =>
        cfg
            .Fields.Where(f => f.Enabled)
            .Select(f => f.TargetName)
            .Concat(
                cfg.Relations.Where(r => r.Enabled)
                    .SelectMany(r => (r.Fields ?? []).Where(f => f.Enabled).Select(f => f.TargetField))
            )
            .ToList();

    public static string BuildConnectionString(ErpConnectionConfig cfg) =>
        $"Host={cfg.Host};Port={cfg.Port};Database={cfg.Database};Username={cfg.Username};Password={cfg.Password};SSL Mode=Prefer;Trust Server Certificate=true;Timeout=5;Command Timeout=10";

    /// <summary>True when this format+config combination should use the nested-JSON query/build path
    /// instead of the flat one — the single decision point shared by Run Now, Preview, and the scheduled
    /// worker so the three callers can never disagree on which shape a mapping produces.</summary>
    public static bool UsesNestedJson(ExportMappingConfig cfg, string format) =>
        format == "json" && (cfg.NestedGroups is { Length: > 0 } || cfg.JsonWrapper is not null);

    public readonly record struct ExportBuildResult(byte[] Bytes, int RecordCount, string Extension);

    /// <summary>
    /// Single execution+build path shared by Run Now and the scheduled ExportWorker: runs the
    /// mapping-driven query (flat or nested-JSON, per <see cref="UsesNestedJson"/>) and serializes it to
    /// the requested format. Previously each caller re-implemented this branch separately and only
    /// Run Now's copy supported nested JSON — Preview and the nightly worker silently fell back to the
    /// flat shape for a nested-group mapping.
    /// </summary>
    public static async Task<ExportBuildResult> BuildExportAsync(
        NpgsqlConnection conn,
        ExportMappingConfig cfg,
        string format,
        string schemaVersion,
        DateTimeOffset extractedAt,
        CancellationToken ct,
        IReadOnlySet<string>? gdprDenylist = null
    )
    {
        if (UsesNestedJson(cfg, format))
        {
            var nestedRecords = await ExecuteNestedJsonQueryAsync(conn, cfg, ct, gdprDenylist: gdprDenylist);
            var nestedBytes = BuildNestedJsonBytes(nestedRecords, cfg.JsonWrapper, schemaVersion, extractedAt);
            return new ExportBuildResult(nestedBytes, nestedRecords.Count, "json");
        }

        var cols = GetColumnNames(cfg);
        var records = await ExecuteQueryAsync(conn, cfg, ct, gdprDenylist: gdprDenylist);
        return format switch
        {
            "csv" => new ExportBuildResult(
                BuildCsvBytes(records, cols, schemaVersion, extractedAt),
                records.Count,
                "csv"
            ),
            "xlsx" => new ExportBuildResult(
                BuildExcelBytes(records, cols, schemaVersion, extractedAt),
                records.Count,
                "xlsx"
            ),
            _ => new ExportBuildResult(BuildJsonBytes(records, schemaVersion, extractedAt), records.Count, "json"),
        };
    }

    public static async Task<List<Dictionary<string, string>>> ExecuteQueryAsync(
        NpgsqlConnection conn,
        ExportMappingConfig cfg,
        CancellationToken ct,
        int? limit = null,
        IReadOnlySet<string>? gdprDenylist = null
    )
    {
        var parts = new List<string>();

        foreach (var f in cfg.Fields.Where(x => x.Enabled))
            parts.Add($"s.{QI(f.SourceName)} AS {QI(f.TargetName)}");

        foreach (var r in cfg.Relations.Where(x => x.Enabled))
        {
            var delim = (r.Delimiter ?? ", ").Replace("'", "''");
            foreach (var f in (r.Fields ?? []).Where(x => x.Enabled))
            {
                var agg =
                    r.FlattenStrategy == "string_join"
                        ? $"string_agg({QI(r.RelatedTable)}.{QI(f.SourceField)}::text, '{delim}')"
                        : $"array_to_string(array_agg({QI(r.RelatedTable)}.{QI(f.SourceField)}::text), ',')";
                parts.Add(
                    $"(SELECT {agg} FROM {QI(r.RelatedTable)} "
                        + $"WHERE {QI(r.RelatedTable)}.{QI(r.JoinKey)} = s.{QI(r.SourceJoinKey)}) AS {QI(f.TargetField)}"
                );
            }
        }

        if (parts.Count == 0)
            return [];

        var sql = $"SELECT {string.Join(", ", parts)} FROM {QI(cfg.SourceTable)} s";
        if (limit.HasValue)
            sql += $" LIMIT {limit.Value}";

        var results = new List<Dictionary<string, string>>();
        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 30 };
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, string>(reader.FieldCount);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (await reader.IsDBNullAsync(i, ct))
                {
                    row[reader.GetName(i)] = "";
                    continue;
                }
                // Coerce date/timestamp columns to ISO 8601 (YYYY-MM-DD) regardless of locale.
                var pgType = reader.GetDataTypeName(i);
                if (pgType is "date" or "timestamp" or "timestamptz")
                    row[reader.GetName(i)] = reader.GetDateTime(i).ToString("yyyy-MM-dd");
                else
                    row[reader.GetName(i)] = reader.GetValue(i)?.ToString() ?? "";
            }
            results.Add(row);
        }

        // Strip any GDPR-denied fields that somehow appeared in the result (defence-in-depth).
        var effectiveDenylist = gdprDenylist ?? GdprDeniedFields;
        foreach (var row in results)
        {
            foreach (var denied in effectiveDenylist)
                row.Remove(denied);
        }

        return results;
    }

    // Single-quote escaping for a value embedded as a JSON key STRING LITERAL inside
    // json_build_object('key', expr, ...). Distinct from QI(), which double-quote-escapes SQL
    // IDENTIFIERS — reusing QI() here would be a correctness bug (Postgres would try to resolve
    // "key" as a column reference instead of treating it as a JSON object key).
    private static string SqlLit(string value) => "'" + value.Replace("'", "''") + "'";

    // Recursively emits a json_build_object(...) expression for an "object" (N:1) group, or a
    // (SELECT json_agg(...) ...) expression for an "array" (1:N) group, recursing into Children so
    // further nested keys are built within the same expression — this is what lets nested groups
    // reach unlimited depth without any depth-specific SQL-building logic.
    private static string BuildNestedGroupExpr(
        ExportMappingNestedGroup g,
        string parentAlias,
        ref int aliasCounter,
        int depth
    )
    {
        if (depth > MaxNestedDepth)
            throw new InvalidOperationException(
                $"Nested group '{g.TargetKey}' exceeds the maximum nesting depth of {MaxNestedDepth}."
            );

        // Synthetic alias (not derived from RelatedTable/TargetKey): avoids alias collisions when two
        // groups join the same related table, and is QI-safe by construction rather than needing
        // identifier validation against admin-supplied text.
        var alias = $"ng{aliasCounter++}";

        var args = new List<string>();
        foreach (var f in g.Fields.Where(x => x.Enabled))
            args.Add($"{SqlLit(f.TargetKey)}, {alias}.{QI(f.SourceField)}");
        foreach (var child in g.Children.Where(x => x.Enabled))
            args.Add($"{SqlLit(child.TargetKey)}, {BuildNestedGroupExpr(child, alias, ref aliasCounter, depth + 1)}");

        var objectExpr = $"json_build_object({string.Join(", ", args)})";
        // json_agg() over zero matching rows returns SQL NULL, not '[]' — without the COALESCE, a
        // manufacturer with no addresses would wrongly serialize as "addresses": null instead of [].
        // Object-kind groups deliberately skip the COALESCE: a genuinely absent N:1 row should become
        // JSON null, which is the correct representation of "no manufacturer".
        var agg = g.Kind == "array" ? $"COALESCE(json_agg({objectExpr}), '[]'::json)" : objectExpr;

        return $"(SELECT {agg} FROM {QI(g.RelatedTable)} {alias} "
            + $"WHERE {alias}.{QI(g.JoinKey)} = {parentAlias}.{QI(g.SourceJoinKey)})";
    }

    /// <summary>
    /// JSON-only sibling of <see cref="ExecuteQueryAsync"/>: builds one query that returns a single
    /// <c>json</c> column per row (top-level fields plus recursively nested groups), using Postgres's
    /// native <c>json_build_object</c>/<c>json_agg</c> to construct the nested tree in SQL rather than
    /// materializing it by hand in C#. Existing flat CSV/Excel/legacy-JSON export is entirely unaffected —
    /// this never calls, and is never called by, <see cref="ExecuteQueryAsync"/>.
    /// </summary>
    public static async Task<List<JsonObject>> ExecuteNestedJsonQueryAsync(
        NpgsqlConnection conn,
        ExportMappingConfig cfg,
        CancellationToken ct,
        int? limit = null,
        IReadOnlySet<string>? gdprDenylist = null
    )
    {
        var args = new List<string>();
        foreach (var f in cfg.Fields.Where(x => x.Enabled))
            args.Add($"{SqlLit(f.TargetName)}, s.{QI(f.SourceName)}");

        var aliasCounter = 0;
        foreach (var g in (cfg.NestedGroups ?? []).Where(x => x.Enabled))
            args.Add($"{SqlLit(g.TargetKey)}, {BuildNestedGroupExpr(g, "s", ref aliasCounter, depth: 1)}");

        var results = new List<JsonObject>();
        if (args.Count == 0)
            return results;

        var sql = $"SELECT json_build_object({string.Join(", ", args)}) AS row_json FROM {QI(cfg.SourceTable)} s";
        if (limit.HasValue)
            sql += $" LIMIT {limit.Value}";

        var effectiveDenylist = gdprDenylist ?? GdprDeniedFields;

        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 30 };
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                // Npgsql's default json/jsonb -> string mapping (no custom type mapping in this repo)
                // returns the raw JSON text; parsing it into a mutable JsonObject (rather than treating it
                // as an opaque string) is what lets it be spliced into the final output tree as real nested
                // JSON instead of a JSON-encoded string-within-a-string.
                var text = await reader.IsDBNullAsync(0, ct) ? "{}" : reader.GetString(0);
                var node = JsonNode.Parse(text) as JsonObject ?? [];
                StripGdprFieldsRecursive(node, effectiveDenylist);
                results.Add(node);
            }
        }
        catch (PostgresException pex) when (pex.SqlState == "21000")
        {
            throw new InvalidOperationException(ObjectGroupCardinalityErrorMessage, pex);
        }

        return results;
    }

    /// <summary>
    /// Surfaced when an "object" (1:N-assumed) nested group's correlated subquery matches more than one
    /// related row for some source row — Postgres raises SQLSTATE 21000 ("more than one row returned by a
    /// subquery used as an expression") because a bare <c>json_build_object(...)</c> subquery, unlike an
    /// "array" group's <c>json_agg(...)</c> one, has no way to hold multiple rows. This turns that opaque
    /// SQL error into an actionable message pointing at the actual fix.
    /// </summary>
    private const string ObjectGroupCardinalityErrorMessage =
        "Nested JSON export failed: an \"object\" nested group matched more than one related row for at "
        + "least one source row. \"object\" groups assume a 1:1 relationship (via JoinKey/SourceJoinKey) between "
        + "the source row and the related table — if a source row can legitimately match multiple related "
        + "rows, change that group's Kind from \"object\" to \"array\" instead.";

    // Walks the parsed JSON tree removing any property whose key matches the GDPR denylist, at every
    // depth. Same "match by output key name" defence-in-depth heuristic the flat ExecuteQueryAsync path
    // already applies post-query, made recursive for nested objects/arrays.
    private static void StripGdprFieldsRecursive(JsonNode? node, IReadOnlySet<string> denylist)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(kv => kv.Key).Where(denylist.Contains).ToList())
                obj.Remove(key);
            foreach (var kv in obj)
                StripGdprFieldsRecursive(kv.Value, denylist);
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
                StripGdprFieldsRecursive(item, denylist);
        }
    }

    // ── ExportNode tree engine (Phase 14) ───────────────────────────────────────
    // Generalizes the nested-JSON path above (BuildNestedGroupExpr/ExecuteNestedJsonQueryAsync) to a
    // single recursive tree walk that also emits plain scalar-field columns, so one query shape serves
    // every output format instead of the legacy flat-vs-nested-JSON fork. There is deliberately no
    // ExportNode counterpart to UsesNestedJson: every ExportNode tree is queried the same way regardless
    // of format (see BuildExportNodeAsync) — only the format WRITER differs (IExportFormatWriter below),
    // which is the actual OCP seam knowledge/pipeline/export-definitions-2.0.md §8 asks for.

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
        IReadOnlySet<string>? gdprDenylist = null
    )
    {
        var records = await ExecuteExportNodeQueryAsync(conn, rootTable, root, ct, limit, gdprDenylist);
        var writer = ExportFormatWriterFactory.Get(format);
        var bytes = writer.Write(root, records, schemaVersion, extractedAt);
        return new ExportBuildResult(bytes, records.Count, writer.FileExtension);
    }

    public static byte[] BuildCsvBytes(
        IReadOnlyList<Dictionary<string, string>> records,
        IReadOnlyList<string> columns,
        string schemaVersion,
        DateTimeOffset extractedAt
    )
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# schema_version={schemaVersion},extracted_at={extractedAt:O}");
        sb.AppendLine(string.Join(",", columns.Select(CsvEscape)));
        foreach (var row in records)
            sb.AppendLine(string.Join(",", columns.Select(c => CsvEscape(row.GetValueOrDefault(c, "")))));
        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static byte[] BuildJsonBytes(
        IReadOnlyList<Dictionary<string, string>> records,
        string schemaVersion,
        DateTimeOffset extractedAt
    )
    {
        var obj = new
        {
            schema_version = schemaVersion,
            extracted_at = extractedAt.ToString("O"),
            records,
        };
        return JsonSerializer.SerializeToUtf8Bytes(obj, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// JSON-only sibling of <see cref="BuildJsonBytes"/> for nested records produced by
    /// <see cref="ExecuteNestedJsonQueryAsync"/>. <see cref="BuildJsonBytes"/> itself is left completely
    /// untouched, so backward compatibility is structural (an old config never reaches this method at
    /// all via <c>PipelineEndpoints</c>), not just "produces equivalent bytes." When <paramref name="wrapper"/>
    /// is null this still reproduces the exact legacy envelope shape as a defensive fallback.
    /// </summary>
    public static byte[] BuildNestedJsonBytes(
        IReadOnlyList<JsonObject> records,
        ExportJsonWrapperConfig? wrapper,
        string schemaVersion,
        DateTimeOffset extractedAt
    )
    {
        var itemsArray = new JsonArray(records.Select(r => (JsonNode?)r.DeepClone()).ToArray());

        if (wrapper is null)
        {
            var legacy = new JsonObject
            {
                ["schema_version"] = schemaVersion,
                ["extracted_at"] = extractedAt.ToString("O"),
                ["records"] = itemsArray,
            };
            return JsonSerializer.SerializeToUtf8Bytes(legacy, new JsonSerializerOptions { WriteIndented = true });
        }

        var itemsKey = string.IsNullOrWhiteSpace(wrapper.ItemsKey) ? "records" : wrapper.ItemsKey;

        var metadata = new JsonObject();
        if (wrapper.MetadataFields is not { Length: > 0 })
        {
            metadata["schema_version"] = schemaVersion;
            metadata["extracted_at"] = extractedAt.ToString("O");
        }
        else
        {
            foreach (var m in wrapper.MetadataFields)
                metadata[m.Key] = m.IsDynamicTimestamp ? extractedAt.ToString("O") : m.Value;
        }

        var inner = new JsonObject();
        if (string.IsNullOrWhiteSpace(wrapper.MetadataKey))
        {
            foreach (var kv in metadata.ToList())
            {
                metadata.Remove(kv.Key);
                inner[kv.Key] = kv.Value;
            }
        }
        else
        {
            inner[wrapper.MetadataKey] = metadata;
        }
        inner[itemsKey] = itemsArray;

        JsonNode root = string.IsNullOrWhiteSpace(wrapper.RootKey)
            ? inner
            : new JsonObject { [wrapper.RootKey] = inner };

        return JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true });
    }

    public static byte[] BuildExcelBytes(
        IReadOnlyList<Dictionary<string, string>> records,
        IReadOnlyList<string> columns,
        string schemaVersion,
        DateTimeOffset extractedAt
    )
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Export");
        ws.Cell(1, 1).Value = $"schema_version={schemaVersion}";
        ws.Cell(1, 2).Value = $"extracted_at={extractedAt:O}";
        ws.Row(1).Style.Font.Bold = true;
        ws.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;
        for (int c = 0; c < columns.Count; c++)
            ws.Cell(2, c + 1).Value = columns[c];
        ws.Row(2).Style.Font.Bold = true;

        // Track columns where all non-empty values are ISO dates so we can apply date format.
        var dateColumns = new HashSet<int>();
        for (int r = 0; r < records.Count; r++)
        {
            for (int c = 0; c < columns.Count; c++)
            {
                var val = records[r].GetValueOrDefault(columns[c], "");
                var cell = ws.Cell(r + 3, c + 1);
                if (
                    !string.IsNullOrEmpty(val)
                    && DateOnly.TryParseExact(
                        val,
                        "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out var date
                    )
                )
                {
                    cell.Value = date.ToDateTime(TimeOnly.MinValue);
                    cell.Style.NumberFormat.Format = "yyyy-mm-dd";
                    dateColumns.Add(c + 1);
                }
                else
                {
                    cell.Value = val;
                    cell.Style.NumberFormat.NumberFormatId = 49; // "@" = Text
                }
            }
        }

        // Text format for all non-date columns (also protects header + metadata rows from auto-conversion).
        for (int c = 1; c <= columns.Count; c++)
            if (!dateColumns.Contains(c))
                ws.Column(c).Style.NumberFormat.NumberFormatId = 49;

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // Safe SQL identifier quoting — wraps in double quotes and escapes embedded double quotes.
    public static string QI(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    /// <summary>MIME type for a <see cref="ExportBuildResult.Extension"/> value, for endpoints that hand
    /// the built bytes straight back in an HTTP response (as opposed to writing them to the staging
    /// folder). Shared by every "run and return the file" endpoint so they can't drift on content type.</summary>
    public static string ContentTypeFor(string extension) =>
        extension switch
        {
            "csv" => "text/csv",
            "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/json",
        };

    /// <summary>Filesystem-safe <c>{slug}_{timestamp}.{extension}</c> download file name for a
    /// free-text name (a preset name, an export definition name — never itself validated as a SQL
    /// identifier, unlike <see cref="QI"/>'s inputs) paired with a build's output extension.</summary>
    public static string BuildNamedFileName(string name, DateTimeOffset extractedAt, string extension)
    {
        var slug = new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        if (slug.Length == 0)
            slug = "export";
        return $"{slug}_{extractedAt:yyyyMMdd'T'HHmmss'Z'}.{extension}";
    }

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return '"' + value.Replace("\"", "\"\"") + '"';
        return value;
    }
}
