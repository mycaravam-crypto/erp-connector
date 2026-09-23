using System.Text.Json.Nodes;
using Connector.Core.DataSources;
using Connector.Core.DynamicExport;
using Connector.Infrastructure;
using Connector.Infrastructure.DataSources;
using Connector.Infrastructure.DataSources.PostgreSql;

namespace Connector.Integration.Tests;

/// <summary>
/// Real-Postgres regression tests for Arbeitsauftrag 6: nested export records are assembled in C# from plain
/// rows (DynamicExportService's tree query engine) instead of by <c>json_build_object</c>/<c>json_agg</c> in
/// SQL. Every expectation below is the exact record the SQL-built version produced for the same tree — the
/// JSON structure must not change. Uses the dedicated <c>export_*</c> tables in testdb/init.sql (orders →
/// customer 1:1, orders → lines 1:n → tags). Same "no-op if the fixture isn't running" convention as every
/// other Postgres-backed test here.
/// </summary>
public sealed class ExportTreeRegressionTests
{
    private static readonly PostgreSqlDataSourceProvider Provider = new();

    private static ExportNode Root(params ExportNode[] children) =>
        new("root", ExportNodeKind.Root, null, null, null, null, null, null, children, true);

    private static ExportNode Scalar(string key, string column) =>
        new(key, ExportNodeKind.ScalarField, column, null, null, null, null, null, [], true);

    private static ExportNode Node(
        string key,
        string kind,
        string table,
        string joinKey,
        string sourceJoinKey,
        params ExportNode[] children
    ) => new(key, kind, null, table, joinKey, sourceJoinKey, null, null, children, true);

    private static ExportNode Customer() =>
        Node(
            "customer",
            ExportNodeKind.Object,
            "export_customer",
            "id",
            "customer_id",
            Scalar("name", "name"),
            Scalar("vip", "vip")
        );

    private static ExportNode Lines(params ExportNode[] extra) =>
        Node(
            "lines",
            ExportNodeKind.Array,
            "export_order_line",
            "order_id",
            "id",
            [Scalar("sku", "sku"), Scalar("qty", "qty"), .. extra]
        );

    private static ExportNode Tags() =>
        Node("tags", ExportNodeKind.Array, "export_line_tag", "line_id", "id", Scalar("tag", "tag"));

    private static Task<List<JsonObject>> RunAsync(ExportNode root, IDataSourceProvider? provider = null) =>
        DynamicExportService.ExecuteExportNodeQueryAsync(
            provider ?? Provider,
            ErpTestFixture.Config,
            "export_order",
            root,
            CancellationToken.None,
            gdprDenylist: new HashSet<string>()
        );

    // Array element order was never guaranteed (neither json_agg nor the per-level queries have an ORDER BY),
    // so records are compared with every array sorted; object key order is compared as-is.
    private static string Canonical(JsonNode? node) => Canonicalize(node)?.ToJsonString() ?? "null";

    private static JsonNode? Canonicalize(JsonNode? node) =>
        node switch
        {
            JsonObject o => new JsonObject(o.Select(kv => KeyValuePair.Create(kv.Key, Canonicalize(kv.Value)))),
            JsonArray a => new JsonArray(
                a.Select(Canonicalize).OrderBy(x => x?.ToJsonString(), StringComparer.Ordinal).ToArray()
            ),
            _ => node?.DeepClone(),
        };

    private static void AssertRecord(string expectedJson, JsonObject actual) =>
        Assert.Equal(Canonical(JsonNode.Parse(expectedJson)), Canonical(actual));

    private static JsonObject Order(List<JsonObject> records, string id) =>
        records.Single(r => r["id"]!.GetValue<string>() == id);

    [Fact]
    public async Task SimpleRootTable_EveryScalarIsItsTextForm()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        var records = await RunAsync(
            Root(Scalar("id", "id"), Scalar("placedOn", "placed_on"), Scalar("total", "total"), Scalar("note", "note"))
        );

        AssertRecord("""{"id":"100","placedOn":"2024-01-05","total":"99.90","note":"rush"}""", Order(records, "100"));
    }

    [Fact]
    public async Task OneToOneRelation_EmbedsSingleObjectAndNullWhenAbsent()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        var records = await RunAsync(Root(Scalar("id", "id"), Customer()));

        AssertRecord("""{"id":"100","customer":{"name":"Acme","vip":"true"}}""", Order(records, "100"));
        // NULL foreign key → no match → JSON null, not an empty object and not a missing key.
        AssertRecord("""{"id":"102","customer":null}""", Order(records, "102"));
    }

    [Fact]
    public async Task OneToManyRelation_GroupsEveryChildUnderItsParent()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        var records = await RunAsync(Root(Scalar("id", "id"), Lines()));

        AssertRecord(
            """{"id":"100","lines":[{"sku":"A-1","qty":"2"},{"sku":"B-2","qty":null}]}""",
            Order(records, "100")
        );
    }

    [Fact]
    public async Task TwoNestingLevels_GroupGrandchildrenUnderTheirOwnChild()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        var records = await RunAsync(Root(Scalar("id", "id"), Customer(), Lines(Tags())));

        AssertRecord(
            """
            {"id":"100","customer":{"name":"Acme","vip":"true"},"lines":[
              {"sku":"A-1","qty":"2","tags":[{"tag":"fragile"},{"tag":"heavy"}]},
              {"sku":"B-2","qty":null,"tags":[]}]}
            """,
            Order(records, "100")
        );
    }

    [Fact]
    public async Task EmptyChildCollection_IsEmptyArrayNotNull()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        var records = await RunAsync(Root(Scalar("id", "id"), Lines(Tags())));

        var lines = Order(records, "101")["lines"];
        Assert.IsType<JsonArray>(lines);
        Assert.Empty(lines!.AsArray());
    }

    [Fact]
    public async Task NullFields_StayPresentAsJsonNull()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        var records = await RunAsync(
            Root(Scalar("id", "id"), Scalar("total", "total"), Scalar("note", "note"), Customer())
        );

        var order = Order(records, "101");
        AssertRecord("""{"id":"101","total":null,"note":null,"customer":{"name":"Acme","vip":"true"}}""", order);
        Assert.True(order.ContainsKey("note"));
    }

    [Fact]
    public async Task MultipleRootRecords_EachGetsOnlyItsOwnChildren()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        var records = await RunAsync(Root(Scalar("id", "id"), Customer(), Lines()));

        Assert.Equal(["100", "101", "102"], records.Select(r => r["id"]!.GetValue<string>()).Order());
        AssertRecord(
            """{"id":"100","customer":{"name":"Acme","vip":"true"},"lines":[{"sku":"A-1","qty":"2"},{"sku":"B-2","qty":null}]}""",
            Order(records, "100")
        );
        AssertRecord("""{"id":"101","customer":{"name":"Acme","vip":"true"},"lines":[]}""", Order(records, "101"));
        AssertRecord("""{"id":"102","customer":null,"lines":[]}""", Order(records, "102"));
        // Orders 100 and 101 share customer 1: two equal but independent objects, not one shared node.
        Assert.NotSame(Order(records, "100")["customer"], Order(records, "101")["customer"]);
    }

    [Fact]
    public async Task QueryCount_IsOnePerTreeNode_AndNoQueryBuildsJson()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        var counting = new CountingProvider(Provider);
        var records = await RunAsync(Root(Scalar("id", "id"), Customer(), Lines(Tags())), counting);

        Assert.Equal(3, records.Count);
        // root + customer + lines + tags — independent of how many orders/lines there are (no N+1).
        Assert.Equal(4, counting.Queries.Count);
        foreach (var sql in counting.Queries)
        {
            Assert.DoesNotContain("json_build_object", sql);
            Assert.DoesNotContain("json_agg", sql);
            Assert.DoesNotContain("array_agg", sql);
            Assert.DoesNotContain("::json", sql);
        }
    }

    [Fact]
    public async Task LegacyNestedGroups_KeepEachColumnsJsonType()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        var cfg = new ExportMappingConfig(
            "export_order",
            [new("id", "id", true), new("placed_on", "placedOn", true), new("total", "total", true)],
            [],
            [
                new ExportMappingNestedGroup(
                    "customer",
                    "export_customer",
                    "id",
                    "customer_id",
                    true,
                    "object",
                    [new("name", "name", true), new("vip", "vip", true)],
                    []
                ),
                new ExportMappingNestedGroup(
                    "lines",
                    "export_order_line",
                    "order_id",
                    "id",
                    true,
                    "array",
                    [new("sku", "sku", true), new("qty", "qty", true)],
                    []
                ),
            ]
        );

        var records = await DynamicExportService.ExecuteNestedJsonQueryAsync(
            Provider,
            ErpTestFixture.Config,
            cfg,
            CancellationToken.None,
            gdprDenylist: new HashSet<string>()
        );

        // Numbers stay JSON numbers (with the column's own scale), booleans stay booleans — exactly the
        // encoding json_build_object gave these columns.
        var byId = records.ToDictionary(r => r["id"]!.GetValue<int>());
        AssertRecord(
            """{"id":100,"placedOn":"2024-01-05","total":99.90,"customer":{"name":"Acme","vip":true},"lines":[{"sku":"A-1","qty":2},{"sku":"B-2","qty":null}]}""",
            byId[100]
        );
        AssertRecord("""{"id":102,"placedOn":"2024-03-01","total":5.00,"customer":null,"lines":[]}""", byId[102]);
    }

    /// <summary>Passes every call through to a real provider, recording the SQL of each query it runs.</summary>
    private sealed class CountingProvider(PostgreSqlDataSourceProvider inner) : ISqlDataSourceProvider
    {
        public List<string> Queries { get; } = [];

        public DataSourceType Type => inner.Type;

        public DataSourceCapabilities Capabilities => inner.Capabilities;

        public ISqlDialect Dialect => inner.Dialect;

        public Task<TestConnectionResult> TestConnectionAsync(
            DataSourceConfig config,
            CancellationToken cancellationToken
        ) => inner.TestConnectionAsync(config, cancellationToken);

        public Task<SourceSchema> ReadSchemaAsync(DataSourceConfig config, CancellationToken cancellationToken) =>
            inner.ReadSchemaAsync(config, cancellationToken);

        public Task<QueryResult> ExecuteAsync(
            DataSourceConfig config,
            SourceQuery query,
            CancellationToken cancellationToken
        ) => inner.ExecuteAsync(config, query, cancellationToken);

        public Task<QueryResult> ExecuteNativeAsync(
            DataSourceConfig config,
            NativeSqlQuery query,
            CancellationToken cancellationToken
        )
        {
            Queries.Add(query.Sql);
            return inner.ExecuteNativeAsync(config, query, cancellationToken);
        }
    }
}
