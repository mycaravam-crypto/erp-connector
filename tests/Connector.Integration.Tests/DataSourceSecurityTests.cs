using System.Text.Json.Nodes;
using Connector.Core.DataSources;
using Connector.Core.DynamicExport;
using Connector.Infrastructure;
using Connector.Infrastructure.DataSources;
using Connector.Infrastructure.DataSources.MariaDb;
using Connector.Infrastructure.DataSources.PostgreSql;

namespace Connector.Integration.Tests;

/// <summary>
/// Arbeitsauftrag 11 security review, executed for real against both SQL backends (the <c>export_*</c> fixture
/// tables in testdb/init.sql and testdb/mariadb-init.sql; no-op per backend when its fixture isn't running):
/// injection payloads in every identifier and value position of a query, and the GDPR denylist applied at
/// every depth of an export tree before any SQL is sent. ServiceNow's equivalents live in
/// <see cref="ServiceNowTableApiProviderTests"/>.
/// </summary>
public sealed class DataSourceSecurityTests
{
    public static TheoryData<string> Backends => new() { "postgres", "mariadb" };

    private static async Task<(ISqlDataSourceProvider Provider, DataSourceConfig Config)?> BackendAsync(string name)
    {
        if (name == "postgres")
            return await ErpTestFixture.IsAvailableAsync()
                ? (new PostgreSqlDataSourceProvider(), ErpTestFixture.Config)
                : null;
        return await MariaDbTestFixture.IsAvailableAsync()
            ? (new MariaDbDataSourceProvider(), MariaDbTestFixture.Config)
            : null;
    }

    // Both quote styles, so the payload tries to break out of either dialect's identifier quoting.
    private const string IdentifierPayload = "export_order\"`; DROP TABLE export_line_tag; --";

    private static async Task AssertFixtureIntactAsync(ISqlDataSourceProvider provider, DataSourceConfig config)
    {
        var schema = await provider.ReadSchemaAsync(config, CancellationToken.None);
        Assert.Contains(schema.Tables, t => t.Name == "export_line_tag");
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task InjectionInTableName_IsRejectedAsUnknown(string backend)
    {
        if (await BackendAsync(backend) is not var (provider, config))
            return;

        await Assert.ThrowsAsync<InvalidSourceQueryException>(() =>
            provider.ExecuteAsync(config, new SourceQuery { RootTable = IdentifierPayload }, CancellationToken.None)
        );
        await AssertFixtureIntactAsync(provider, config);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task InjectionInColumnName_IsRejectedAsUnknown(string backend)
    {
        if (await BackendAsync(backend) is not var (provider, config))
            return;

        await Assert.ThrowsAsync<InvalidSourceQueryException>(() =>
            provider.ExecuteAsync(
                config,
                new SourceQuery
                {
                    RootTable = "export_order",
                    Conditions = [new QueryCondition { Column = IdentifierPayload, Operator = QueryOperator.IsNull }],
                },
                CancellationToken.None
            )
        );
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task InjectionInJoinField_IsRejectedAsUnknown(string backend)
    {
        if (await BackendAsync(backend) is not var (provider, config))
            return;

        await Assert.ThrowsAsync<InvalidSourceQueryException>(() =>
            provider.ExecuteAsync(
                config,
                new SourceQuery
                {
                    RootTable = "export_order",
                    Joins =
                    [
                        new QueryJoin
                        {
                            Table = "export_customer",
                            Column = "id",
                            ParentColumn = IdentifierPayload,
                        },
                    ],
                },
                CancellationToken.None
            )
        );
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task InjectionInFilterAndInValues_IsBoundAsPlainData(string backend)
    {
        if (await BackendAsync(backend) is not var (provider, config))
            return;

        async Task<int> CountAsync(QueryCondition condition) =>
            (
                await provider.ExecuteAsync(
                    config,
                    new SourceQuery { RootTable = "export_order", Conditions = [condition] },
                    CancellationToken.None
                )
            )
                .Rows
                .Count;

        Assert.Equal(
            0,
            await CountAsync(
                new QueryCondition
                {
                    Column = "note",
                    Operator = QueryOperator.Equal,
                    Value = "x' OR '1'='1",
                }
            )
        );
        Assert.Equal(
            0,
            await CountAsync(
                new QueryCondition
                {
                    Column = "note",
                    Operator = QueryOperator.Contains,
                    Value = "'; DROP TABLE export_line_tag; --",
                }
            )
        );
        Assert.Equal(
            1,
            await CountAsync(
                new QueryCondition
                {
                    Column = "note",
                    Operator = QueryOperator.In,
                    Values = ["rush", "x') OR ('1'='1"],
                }
            )
        );
        await AssertFixtureIntactAsync(provider, config);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task InjectionInExportNodeIdentifier_IsQuotedAndFailsAsUnknownTable(string backend)
    {
        if (await BackendAsync(backend) is not var (provider, config))
            return;

        var root = new ExportNode(
            "root",
            ExportNodeKind.Root,
            null,
            null,
            null,
            null,
            null,
            null,
            [new ExportNode("id", ExportNodeKind.ScalarField, "id", null, null, null, null, null, [], true)],
            true
        );

        await Assert.ThrowsAsync<DataSourceQueryException>(() =>
            DynamicExportService.ExecuteExportNodeQueryAsync(
                provider,
                config,
                IdentifierPayload,
                root,
                CancellationToken.None,
                gdprDenylist: new HashSet<string>()
            )
        );
        await AssertFixtureIntactAsync(provider, config);
    }

    // ── GDPR ─────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task GdprDenylist_ExcludesRootChildAndNestedFields_FromEverySelect(string backend)
    {
        if (await BackendAsync(backend) is not var (inner, config))
            return;

        static ExportNode Scalar(string key, string column) =>
            new(key, ExportNodeKind.ScalarField, column, null, null, null, null, null, [], true);
        static ExportNode Node(
            string key,
            string kind,
            string table,
            string joinKey,
            string sourceJoinKey,
            params ExportNode[] children
        ) => new(key, kind, null, table, joinKey, sourceJoinKey, null, null, children, true);

        var root = new ExportNode(
            "root",
            ExportNodeKind.Root,
            null,
            null,
            null,
            null,
            null,
            null,
            [
                Scalar("id", "id"),
                Scalar("remark", "note"), // root, renamed away from its column name
                Node(
                    "customer",
                    ExportNodeKind.Object,
                    "export_customer",
                    "id",
                    "customer_id",
                    Scalar("vip", "vip"),
                    Scalar("who", "name")
                ),
                Node(
                    "lines",
                    ExportNodeKind.Array,
                    "export_order_line",
                    "order_id",
                    "id",
                    Scalar("sku", "sku"),
                    Node("tags", ExportNodeKind.Array, "export_line_tag", "line_id", "id", Scalar("label", "tag"))
                ),
            ],
            true
        );
        var recording = new RecordingProvider(inner);

        var records = await DynamicExportService.ExecuteExportNodeQueryAsync(
            recording,
            config,
            "export_order",
            root,
            CancellationToken.None,
            gdprDenylist: new HashSet<string> { "note", "name", "tag" }
        );

        Assert.Equal(4, recording.Queries.Count);
        foreach (var sql in recording.Queries)
        {
            var quote = inner.Dialect.QuoteIdentifier("x")[0];
            Assert.DoesNotContain($".{quote}note{quote}", sql);
            Assert.DoesNotContain($".{quote}name{quote}", sql);
            Assert.DoesNotContain($".{quote}tag{quote}", sql);
        }

        var order = records.Single(r => r["id"]!.GetValue<string>() == "100");
        Assert.False(order.ContainsKey("remark"));
        Assert.False(order["customer"]!.AsObject().ContainsKey("who"));
        var line = order["lines"]!.AsArray().OfType<JsonObject>().Single(l => l["sku"]!.GetValue<string>() == "A-1");
        Assert.All(line["tags"]!.AsArray(), t => Assert.Empty(t!.AsObject()));
    }

    private sealed class RecordingProvider(ISqlDataSourceProvider inner) : ISqlDataSourceProvider
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
