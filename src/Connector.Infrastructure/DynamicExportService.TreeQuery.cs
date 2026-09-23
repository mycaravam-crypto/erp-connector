using System.Text.Json.Nodes;
using Connector.Core.DataSources;
using Connector.Infrastructure.DataSources;

namespace Connector.Infrastructure;

// ── Tree query engine (Arbeitsauftrag 6) ─────────────────────────────────────
// Builds nested export records in C# from plain relational rows — the database no longer assembles any JSON.
// Both nested paths compile their own config shape into a TreePlan (the ExportNode tree engine in
// DynamicExportService.ExportNode.cs, the legacy nested-group JSON export in .LegacyMapping.cs) and hand it
// here:
//
//   Database → rows (one query per tree node) → QueryResult → grouped by join key in C# → JsonObject tree
//
// Each node is fetched with exactly one query for its whole level (chunked for very large key sets): a child
// node's query selects every related row whose join key is among the join-key values of *all* parent rows
// fetched so far (dialect.BuildMatchesAny), so the number of queries depends on the size of the tree, never on
// the number of rows — no N+1. Rows are grouped by the text of the join key on both sides, which is the key
// the database itself compared on.
public static partial class DynamicExportService
{
    /// <summary>How many distinct parent keys one child query matches at most; a level with more parents is
    /// fetched in several queries of this size.</summary>
    internal const int TreeChildKeyBatchSize = 10_000;

    /// <summary>One table of a compiled export tree: its rows become JSON objects with <see cref="Members"/>
    /// as keys, in order.</summary>
    /// <param name="Alias">The SQL alias the table gets in its query — kept identical to the alias the
    /// pre-Arbeitsauftrag-6 SQL used for this node (<c>s</c> for the root, <c>en0</c>/<c>ng0</c>… in
    /// pre-order below it), since a stored <see cref="Filter"/> may refer to it.</param>
    /// <param name="Filter">A stored WHERE fragment scoped to this node's own rows, or null.</param>
    /// <param name="CastScalarsToText">True renders every scalar as a JSON string of its SQL <c>::text</c>
    /// form (the ExportNode engine's contract); false renders it as the database's own JSON encoding of the
    /// column type would (the legacy nested-group contract — numbers stay numbers, etc.).</param>
    private sealed record TreePlan(
        string Table,
        string Alias,
        string? Filter,
        IReadOnlyList<TreeMember> Members,
        bool CastScalarsToText
    );

    private abstract record TreeMember(string Key);

    private sealed record TreeScalar(string Key, string Column) : TreeMember(Key);

    /// <summary>A nested object/array: rows of <see cref="Node"/>'s table whose <see cref="JoinKey"/> equals the
    /// parent row's <see cref="SourceJoinKey"/>. An array holds every match (<c>[]</c> for none); an object
    /// holds the single match (<c>null</c> for none, <see cref="CardinalityErrorMessage"/> for several).</summary>
    private sealed record TreeChild(
        string Key,
        bool IsArray,
        string JoinKey,
        string SourceJoinKey,
        TreePlan Node,
        string CardinalityErrorMessage
    ) : TreeMember(Key);

    private sealed record TreeRow(string? JoinKeyValue, JsonObject Record);

    /// <summary>Fetches <paramref name="root"/>'s rows (at most <paramref name="limit"/>) and every nested level
    /// below them, and assembles one <see cref="JsonObject"/> per root row, in the root query's row order.</summary>
    private static async Task<List<JsonObject>> ExecuteTreeQueryAsync(
        IDataSourceProvider provider,
        DataSourceConfig dsConfig,
        TreePlan root,
        int? limit,
        CancellationToken ct
    )
    {
        if (root.Members.Count == 0)
            return [];

        var dialect = DialectOf(provider);
        var rows = await FetchTreeLevelAsync(provider, dsConfig, dialect, root, null, null, limit, ct);
        return rows.Select(r => r.Record).ToList();
    }

    // Fetches one node's rows — all of them for the root, or those whose joinKeyColumn is in parentKeys for a
    // child — then recursively its children's rows for all of them at once, and assembles the records.
    private static async Task<List<TreeRow>> FetchTreeLevelAsync(
        IDataSourceProvider provider,
        DataSourceConfig dsConfig,
        ISqlDialect dialect,
        TreePlan plan,
        string? joinKeyColumn,
        IReadOnlyList<string>? parentKeys,
        int? limit,
        CancellationToken ct
    )
    {
        QueryResult[] results;
        if (parentKeys is null)
        {
            results = [await RunTreeQueryAsync(provider, dsConfig, dialect, plan, null, null, limit, ct)];
        }
        else
        {
            results = new QueryResult[(parentKeys.Count + TreeChildKeyBatchSize - 1) / TreeChildKeyBatchSize];
            for (var i = 0; i < results.Length; i++)
            {
                var batch = parentKeys.Skip(i * TreeChildKeyBatchSize).Take(TreeChildKeyBatchSize).ToArray();
                results[i] = await RunTreeQueryAsync(provider, dsConfig, dialect, plan, joinKeyColumn, batch, null, ct);
            }
        }

        var columns = results[0].Columns;
        var rows = results.SelectMany(r => r.Rows).ToList();

        // One lookup per child member: every child row fetched for this whole level, keyed by its join key.
        var childLookups = new ILookup<string, JsonObject>?[plan.Members.Count];
        for (var m = 0; m < plan.Members.Count; m++)
        {
            if (plan.Members[m] is not TreeChild child)
                continue;

            var keys = rows.Select(r => r.Values[m]).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
            var childRows =
                keys.Count == 0
                    ? []
                    : await FetchTreeLevelAsync(provider, dsConfig, dialect, child.Node, child.JoinKey, keys, null, ct);
            childLookups[m] = childRows.ToLookup(r => r.JoinKeyValue!, r => r.Record, StringComparer.Ordinal);
        }

        var assembled = new List<TreeRow>(rows.Count);
        foreach (var values in rows.Select(r => r.Values))
        {
            var record = new JsonObject();
            for (var m = 0; m < plan.Members.Count; m++)
            {
                var value = values[m];
                // JsonObject.Add (not the indexer) on purpose: a tree with two members under the same key has
                // always failed rather than silently keeping one of them.
                record.Add(
                    plan.Members[m].Key,
                    plan.Members[m] switch
                    {
                        TreeChild child => AssembleChild(child, value, childLookups[m]!),
                        _ => value is null
                            ? null
                            : dialect.ConvertNativeTextToJson(value, columns[m].DataType ?? "text"),
                    }
                );
            }
            assembled.Add(new TreeRow(joinKeyColumn is null ? null : values[plan.Members.Count], record));
        }
        return assembled;
    }

    private static JsonNode? AssembleChild(TreeChild child, string? parentKey, ILookup<string, JsonObject> lookup)
    {
        // A NULL parent key never equals anything — same as the SQL join it replaces.
        var matches = parentKey is null ? [] : lookup[parentKey].ToList();
        if (child.IsArray)
            return new JsonArray(matches.Select(Detached).ToArray());

        return matches.Count switch
        {
            0 => null,
            1 => Detached(matches[0]),
            _ => throw new InvalidOperationException(child.CardinalityErrorMessage),
        };
    }

    // The same related row can belong to several parents (e.g. two items sharing one manufacturer); a JsonNode
    // can only have one parent, so every further use gets its own copy.
    private static JsonNode Detached(JsonObject record) => record.Parent is null ? record : record.DeepClone();

    // SELECT <member columns>[, <join key>] FROM <table> <alias> [WHERE <join key> IN <parent keys> AND (<filter>)]
    // [LIMIT n]. Column i of the result is member i: a scalar's value, or a child's parent-side join key; the
    // optional last column is this row's own join key. Keys are compared and returned as text so both sides of
    // the grouping agree on one representation.
    private static Task<QueryResult> RunTreeQueryAsync(
        IDataSourceProvider provider,
        DataSourceConfig dsConfig,
        ISqlDialect dialect,
        TreePlan plan,
        string? joinKeyColumn,
        string[]? parentKeys,
        int? limit,
        CancellationToken ct
    )
    {
        string Column(string name) => $"{plan.Alias}.{dialect.QuoteIdentifier(name)}";

        var select = plan
            .Members.Select(member =>
                member switch
                {
                    TreeChild child => dialect.CastToText(Column(child.SourceJoinKey)),
                    TreeScalar scalar when plan.CastScalarsToText => dialect.CastToText(Column(scalar.Column)),
                    TreeScalar scalar => Column(scalar.Column),
                    _ => throw new InvalidOperationException($"Unknown tree member '{member.Key}'."),
                }
            )
            .ToList();
        if (joinKeyColumn is not null)
            select.Add(dialect.CastToText(Column(joinKeyColumn)));

        var conditions = new List<string>();
        var parameters = new Dictionary<string, object?>();
        if (joinKeyColumn is not null)
        {
            conditions.Add(dialect.BuildMatchesAny(dialect.CastToText(Column(joinKeyColumn)), parentKeys!, parameters));
        }
        if (!string.IsNullOrWhiteSpace(plan.Filter))
            conditions.Add($"({plan.Filter})");

        var sql =
            $"SELECT {string.Join(", ", select.Select((expr, i) => $"{expr} AS {dialect.QuoteIdentifier($"c{i}")}"))} "
            + $"FROM {dialect.QuoteIdentifier(plan.Table)} {plan.Alias}";
        if (conditions.Count > 0)
            sql += " WHERE " + string.Join(" AND ", conditions);
        if (limit.HasValue)
            sql += " " + dialect.BuildLimit(limit.Value);

        return provider.ExecuteNativeAsync(
            dsConfig,
            new NativeSqlQuery(sql, parameters, CommandTimeoutSeconds: 30, ReturnNativeText: true),
            ct
        );
    }
}
