using System.Text.Json.Nodes;
using Connector.Core.DataSources;
using Connector.Core.DynamicExport;
using Connector.Infrastructure.DataSources;

namespace Connector.Infrastructure;

// The original ExportMappingConfig-based single-mapping pipeline: flat CSV/Excel/JSON export, plus a
// JSON-only nested-group extension built straight in SQL (the dialect's JSON object/array aggregation). Still live — served
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
        IDataSourceProvider provider,
        DataSourceConfig dsConfig,
        ExportMappingConfig cfg,
        string format,
        string schemaVersion,
        DateTimeOffset extractedAt,
        CancellationToken ct,
        IReadOnlySet<string>? gdprDenylist = null
    )
    {
        var metered = new MeteredDataSourceProvider(provider);
        var built = await BuildExportUnmeteredAsync(
            metered,
            dsConfig,
            cfg,
            format,
            schemaVersion,
            extractedAt,
            ct,
            gdprDenylist
        );
        return built with { Metrics = metered.Metrics };
    }

    private static async Task<ExportBuildResult> BuildExportUnmeteredAsync(
        IDataSourceProvider provider,
        DataSourceConfig dsConfig,
        ExportMappingConfig cfg,
        string format,
        string schemaVersion,
        DateTimeOffset extractedAt,
        CancellationToken ct,
        IReadOnlySet<string>? gdprDenylist
    )
    {
        if (UsesNestedJson(cfg, format))
        {
            var nestedRecords = await ExecuteNestedJsonQueryAsync(
                provider,
                dsConfig,
                cfg,
                ct,
                gdprDenylist: gdprDenylist
            );
            var nestedBytes = BuildNestedJsonBytes(nestedRecords, cfg.JsonWrapper, schemaVersion, extractedAt);
            return new ExportBuildResult(nestedBytes, nestedRecords.Count, "json");
        }

        var cols = GetColumnNames(cfg);
        var records = await ExecuteQueryAsync(provider, dsConfig, cfg, ct, gdprDenylist: gdprDenylist);
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
        IDataSourceProvider provider,
        DataSourceConfig dsConfig,
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
        var dialect = DialectOf(provider);
        string QI(string identifier) => dialect.QuoteIdentifier(identifier);

        var parts = new List<string>();

        foreach (var f in cfg.Fields.Where(x => x.Enabled && !effectiveDenylist.Contains(x.SourceName)))
            parts.Add($"s.{QI(f.SourceName)} AS {QI(f.TargetName)}");

        foreach (var r in cfg.Relations.Where(x => x.Enabled))
        {
            // "string_join" joins with the relation's own delimiter; any other strategy ("array") with a plain
            // comma. (The "array" form used to be array_to_string(array_agg(x::text), ','), which differs from
            // string_agg only by yielding '' instead of NULL when every related value is NULL — and both reach
            // the caller as "" via the null-to-empty conversion below.)
            var delim = r.FlattenStrategy == "string_join" ? r.Delimiter ?? ", " : ",";
            foreach (var f in (r.Fields ?? []).Where(x => x.Enabled && !effectiveDenylist.Contains(x.SourceField)))
            {
                var agg = dialect.BuildStringAggregate($"{QI(r.RelatedTable)}.{QI(f.SourceField)}", delim);
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
            sql += " " + dialect.BuildLimit(limit.Value);

        // Date/timestamp columns already arrive ISO-8601-coerced (YYYY-MM-DD) from the provider — same rule
        // the pre-abstraction reader loop applied inline here. A NULL column becomes "" (not the provider's
        // own null), matching this method's long-standing contract for the legacy flat CSV/Excel/JSON export.
        var queryResult = await provider.ExecuteNativeAsync(
            dsConfig,
            new NativeSqlQuery(sql, CommandTimeoutSeconds: 30),
            ct
        );
        var results = queryResult
            .ToDictionaries()
            .Select(row => row.ToDictionary(kv => kv.Key, kv => kv.Value ?? ""))
            .ToList();

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

    // Compiles one nested group (and, recursively, its Children) into a TreePlan for the tree query engine
    // (DynamicExportService.TreeQuery.cs): "array" groups become arrays of objects, every other Kind a single
    // embedded object. Fields keep their column's own JSON type (CastScalarsToText: false) — a number stays a
    // number, as it did when the database built this JSON. Fully built, depth check included, before any query.
    private static TreePlan BuildNestedGroupPlan(
        ExportMappingNestedGroup g,
        string alias,
        ref int aliasCounter,
        int depth,
        IReadOnlySet<string> denylist
    )
    {
        if (depth > MaxNestedDepth)
            throw new InvalidOperationException(
                $"Nested group '{g.TargetKey}' exceeds the maximum nesting depth of {MaxNestedDepth}."
            );

        var members = new List<TreeMember>();
        // GDPR denylist check keyed on SourceField, not TargetKey — see the matching comment in
        // DynamicExportService.ExportNode.cs's BuildExportNodePlan (security-review finding SR-08).
        foreach (var f in g.Fields.Where(x => x.Enabled && !denylist.Contains(x.SourceField)))
            members.Add(new TreeScalar(f.TargetKey, f.SourceField));
        foreach (var child in g.Children.Where(x => x.Enabled))
            members.Add(NestedGroupMember(child, ref aliasCounter, depth + 1, denylist));

        return new TreePlan(g.RelatedTable, alias, Filter: null, members, CastScalarsToText: false);
    }

    private static TreeChild NestedGroupMember(
        ExportMappingNestedGroup g,
        ref int aliasCounter,
        int depth,
        IReadOnlySet<string> denylist
    )
    {
        // Synthetic alias, numbered in pre-order like the correlated subqueries this replaced.
        var alias = $"ng{aliasCounter++}";
        return new TreeChild(
            g.TargetKey,
            IsArray: g.Kind == "array",
            g.JoinKey,
            g.SourceJoinKey,
            BuildNestedGroupPlan(g, alias, ref aliasCounter, depth, denylist),
            ObjectGroupCardinalityErrorMessage
        );
    }

    /// <summary>
    /// JSON-only sibling of <see cref="ExecuteQueryAsync"/>: returns one JSON object per source row — top-level
    /// fields plus recursively nested groups — assembled in C# from plain rows by the tree query engine (one
    /// query per group, never per row). Values keep their column's JSON type exactly as PostgreSQL's own JSON
    /// encoding gave them when this tree used to be built in SQL. Existing flat CSV/Excel/legacy-JSON export is
    /// entirely unaffected — this never calls, and is never called by, <see cref="ExecuteQueryAsync"/>.
    /// </summary>
    public static async Task<List<JsonObject>> ExecuteNestedJsonQueryAsync(
        IDataSourceProvider provider,
        DataSourceConfig dsConfig,
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

        var members = new List<TreeMember>();
        foreach (var f in cfg.Fields.Where(x => x.Enabled && !effectiveDenylist.Contains(x.SourceName)))
            members.Add(new TreeScalar(f.TargetName, f.SourceName));

        var aliasCounter = 0;
        foreach (var g in (cfg.NestedGroups ?? []).Where(x => x.Enabled))
            members.Add(NestedGroupMember(g, ref aliasCounter, depth: 1, effectiveDenylist));

        var plan = new TreePlan(cfg.SourceTable, "s", Filter: null, members, CastScalarsToText: false);
        var results = await ExecuteTreeQueryAsync(provider, dsConfig, plan, limit, ct);
        foreach (var node in results)
            StripGdprFieldsRecursive(node, effectiveDenylist);
        return results;
    }

    /// <summary>
    /// Surfaced when an "object" (1:1-assumed) nested group matches more than one related row for some source
    /// row — an object has room for exactly one. Points at the actual fix instead of silently picking a row.
    /// </summary>
    private const string ObjectGroupCardinalityErrorMessage =
        "Nested JSON export failed: an \"object\" nested group matched more than one related row for at "
        + "least one source row. \"object\" groups assume a 1:1 relationship (via JoinKey/SourceJoinKey) between "
        + "the source row and the related table — if a source row can legitimately match multiple related "
        + "rows, change that group's Kind from \"object\" to \"array\" instead.";
}
