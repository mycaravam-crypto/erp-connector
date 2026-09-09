using System.Text.Json.Nodes;
using Connector.Core.DynamicExport;
using Npgsql;

namespace Connector.Infrastructure;

// The original ExportMappingConfig-based single-mapping pipeline: flat CSV/Excel/JSON export, plus a
// JSON-only nested-group extension built straight in SQL (json_build_object/json_agg). Still live — served
// today by /api/pipeline/* (PipelineEndpoints) and its SchemaView.vue/ExportView.vue frontend callers —
// running alongside the newer ExportNode tree engine (DynamicExportService.ExportNode.cs) rather than
// having been superseded by it. Don't delete any of this without first confirming those endpoints and
// their frontend callers are being retired too.
public static partial class DynamicExportService
{
    public static IReadOnlyList<string> GetColumnNames(ExportMappingConfig cfg) =>
        cfg
            .Fields.Where(f => f.Enabled)
            .Select(f => f.TargetName)
            .Concat(
                cfg.Relations.Where(r => r.Enabled)
                    .SelectMany(r => (r.Fields ?? []).Where(f => f.Enabled).Select(f => f.TargetField))
            )
            .ToList();

    /// <summary>True when this format+config combination should use the nested-JSON query/build path
    /// instead of the flat one — the single decision point shared by Run Now, Preview, and the scheduled
    /// worker so the three callers can never disagree on which shape a mapping produces.</summary>
    public static bool UsesNestedJson(ExportMappingConfig cfg, string format) =>
        format == "json" && (cfg.NestedGroups is { Length: > 0 } || cfg.JsonWrapper is not null);

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
        // Computed up front (not just applied post-query, further down) so a GDPR-denylisted source column
        // — keyed on SourceName/SourceField, not the export's TargetName/TargetField alias — is excluded
        // from the SELECT list entirely and never leaves the database. Re-evaluated against the *current*
        // denylist on every run, so a mapping saved before a field was denylisted is covered too, not just
        // newly-saved ones (security-review finding SR-08).
        var effectiveDenylist = gdprDenylist ?? GdprDeniedFields;

        var parts = new List<string>();

        foreach (var f in cfg.Fields.Where(x => x.Enabled && !effectiveDenylist.Contains(x.SourceName)))
            parts.Add($"s.{QI(f.SourceName)} AS {QI(f.TargetName)}");

        foreach (var r in cfg.Relations.Where(x => x.Enabled))
        {
            var delim = (r.Delimiter ?? ", ").Replace("'", "''");
            foreach (var f in (r.Fields ?? []).Where(x => x.Enabled && !effectiveDenylist.Contains(x.SourceField)))
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

        // Second, independent layer of defence-in-depth on top of the SELECT-list exclusion above — catches
        // a denied field under a TargetName that happens to match its own denylist entry (or any other
        // path that isn't the SourceName/SourceField exclusion above).
        foreach (var row in results)
        {
            foreach (var denied in effectiveDenylist)
                row.Remove(denied);
        }

        return results;
    }

    // Recursively emits a json_build_object(...) expression for an "object" (N:1) group, or a
    // (SELECT json_agg(...) ...) expression for an "array" (1:N) group, recursing into Children so
    // further nested keys are built within the same expression — this is what lets nested groups
    // reach unlimited depth without any depth-specific SQL-building logic.
    private static string BuildNestedGroupExpr(
        ExportMappingNestedGroup g,
        string parentAlias,
        ref int aliasCounter,
        int depth,
        IReadOnlySet<string> denylist
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
        // GDPR denylist check keyed on SourceField, not TargetKey — see the matching comment in
        // DynamicExportService.ExportNode.cs's BuildExportNodeExpr (security-review finding SR-08).
        foreach (var f in g.Fields.Where(x => x.Enabled && !denylist.Contains(x.SourceField)))
            args.Add($"{SqlLit(f.TargetKey)}, {alias}.{QI(f.SourceField)}");
        foreach (var child in g.Children.Where(x => x.Enabled))
            args.Add(
                $"{SqlLit(child.TargetKey)}, {BuildNestedGroupExpr(child, alias, ref aliasCounter, depth + 1, denylist)}"
            );

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
        // Computed up front — see ExecuteQueryAsync's matching comment (security-review finding SR-08):
        // excludes a denylisted source column from the SELECT list entirely, re-evaluated against the
        // *current* denylist on every run.
        var effectiveDenylist = gdprDenylist ?? GdprDeniedFields;

        var args = new List<string>();
        foreach (var f in cfg.Fields.Where(x => x.Enabled && !effectiveDenylist.Contains(x.SourceName)))
            args.Add($"{SqlLit(f.TargetName)}, s.{QI(f.SourceName)}");

        var aliasCounter = 0;
        foreach (var g in (cfg.NestedGroups ?? []).Where(x => x.Enabled))
            args.Add(
                $"{SqlLit(g.TargetKey)}, {BuildNestedGroupExpr(g, "s", ref aliasCounter, depth: 1, effectiveDenylist)}"
            );

        var results = new List<JsonObject>();
        if (args.Count == 0)
            return results;

        var sql = $"SELECT json_build_object({string.Join(", ", args)}) AS row_json FROM {QI(cfg.SourceTable)} s";
        if (limit.HasValue)
            sql += $" LIMIT {limit.Value}";

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
}
