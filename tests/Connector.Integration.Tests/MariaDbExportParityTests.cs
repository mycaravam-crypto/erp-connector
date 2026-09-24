using System.Text;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using Connector.Core.DataSources;
using Connector.Core.DynamicExport;
using Connector.Infrastructure;
using Connector.Infrastructure.DataSources.MariaDb;
using Connector.Infrastructure.DataSources.PostgreSql;

namespace Connector.Integration.Tests;

/// <summary>
/// One identical export definition produces the same result against
/// PostgreSQL and MariaDB. Runs every tree against the <c>export_*</c> tables of both fixtures (testdb/init.sql,
/// testdb/mariadb-init.sql — the same rows) and compares the output. No-ops unless both are running.
/// </summary>
/// <remarks>
/// One known, documented difference is left out of the trees on purpose: MariaDB has no boolean type
/// (<c>BOOLEAN</c> is <c>TINYINT(1)</c>), so an <see cref="ExportNode"/> scalar of a boolean column is
/// <c>"1"</c>/<c>"0"</c> there and <c>"true"</c>/<c>"false"</c> in PostgreSQL — see
/// <see cref="BooleanScalar_IsTextOfTheBackendsOwnRepresentation"/>.
/// </remarks>
public sealed class MariaDbExportParityTests
{
    private static readonly IDataSourceProvider Postgres = new PostgreSqlDataSourceProvider();
    private static readonly IDataSourceProvider MariaDb = new MariaDbDataSourceProvider();
    private static readonly DateTimeOffset ExtractedAt = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static async Task<bool> BothAvailableAsync() =>
        await ErpTestFixture.IsAvailableAsync() && await MariaDbTestFixture.IsAvailableAsync();

    private static DataSourceConfig ConfigFor(IDataSourceProvider provider) =>
        provider == MariaDb ? MariaDbTestFixture.Config : ErpTestFixture.Config;

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
        string? filter,
        params ExportNode[] children
    ) => new(key, kind, null, table, joinKey, sourceJoinKey, filter, null, children, true);

    private static ExportNode Customer() =>
        Node("customer", ExportNodeKind.Object, "export_customer", "id", "customer_id", null, Scalar("name", "name"));

    private static ExportNode Lines(string? filter = null) =>
        Node(
            "lines",
            ExportNodeKind.Array,
            "export_order_line",
            "order_id",
            "id",
            filter,
            Scalar("sku", "sku"),
            Scalar("qty", "qty"),
            Node("tags", ExportNodeKind.Array, "export_line_tag", "line_id", "id", null, Scalar("tag", "tag"))
        );

    private static ExportNode FlatOrder() =>
        Root(Scalar("id", "id"), Scalar("placedOn", "placed_on"), Scalar("total", "total"), Scalar("note", "note"));

    private static ExportNode NestedOrder() => Root(Scalar("id", "id"), Scalar("total", "total"), Customer(), Lines());

    private static ExportNode WithRootFilter(ExportNode root, string filter) => root with { Filter = filter };

    private static Task<List<JsonObject>> RecordsAsync(IDataSourceProvider provider, ExportNode root) =>
        DynamicExportService.ExecuteExportNodeQueryAsync(
            provider,
            ConfigFor(provider),
            "export_order",
            root,
            CancellationToken.None,
            gdprDenylist: new HashSet<string>()
        );

    private static async Task<byte[]> BytesAsync(IDataSourceProvider provider, ExportNode root, string format) =>
        (
            await DynamicExportService.BuildExportNodeAsync(
                provider,
                ConfigFor(provider),
                "export_order",
                root,
                format,
                "v1",
                ExtractedAt,
                CancellationToken.None,
                gdprDenylist: new HashSet<string>()
            )
        ).Bytes;

    // Neither backend guarantees row or array order (no ORDER BY), so records and arrays are compared sorted.
    private static string Canonical(IEnumerable<JsonNode?> records) =>
        string.Join("\n", records.Select(r => Canonicalize(r)?.ToJsonString()).Order(StringComparer.Ordinal));

    private static JsonNode? Canonicalize(JsonNode? node) =>
        node switch
        {
            JsonObject o => new JsonObject(o.Select(kv => KeyValuePair.Create(kv.Key, Canonicalize(kv.Value)))),
            JsonArray a => new JsonArray(
                a.Select(Canonicalize).OrderBy(x => x?.ToJsonString(), StringComparer.Ordinal).ToArray()
            ),
            _ => node?.DeepClone(),
        };

    private static async Task AssertSameRecordsAsync(ExportNode root, int expectedCount)
    {
        var postgres = await RecordsAsync(Postgres, root);
        var mariaDb = await RecordsAsync(MariaDb, root);

        Assert.Equal(expectedCount, postgres.Count);
        Assert.Equal(Canonical(postgres), Canonical(mariaDb));
    }

    [Fact]
    public async Task FlatExport_SameRecords()
    {
        if (!await BothAvailableAsync())
            return;

        await AssertSameRecordsAsync(FlatOrder(), 3);
    }

    [Fact]
    public async Task Filter_SameRecords()
    {
        if (!await BothAvailableAsync())
            return;

        await AssertSameRecordsAsync(WithRootFilter(FlatOrder(), "total > 10 OR note = 'rush'"), 1);
        await AssertSameRecordsAsync(WithRootFilter(NestedOrder(), "customer_id IS NOT NULL"), 2);
    }

    [Fact]
    public async Task Join_SameRecords()
    {
        if (!await BothAvailableAsync())
            return;

        await AssertSameRecordsAsync(Root(Scalar("id", "id"), Customer()), 3);
    }

    [Fact]
    public async Task NestedExport_SameRecords()
    {
        if (!await BothAvailableAsync())
            return;

        await AssertSameRecordsAsync(NestedOrder(), 3);
        await AssertSameRecordsAsync(Root(Scalar("id", "id"), Lines(filter: "qty IS NOT NULL"), Customer()), 3);
    }

    [Fact]
    public async Task Csv_SameContent()
    {
        if (!await BothAvailableAsync())
            return;

        static string[] Lines(byte[] bytes) =>
            Encoding.UTF8.GetString(bytes).Split('\n').Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(
            Lines(await BytesAsync(Postgres, NestedOrder(), "csv")),
            Lines(await BytesAsync(MariaDb, NestedOrder(), "csv"))
        );
    }

    [Fact]
    public async Task Json_SameContent()
    {
        if (!await BothAvailableAsync())
            return;

        static string Normalize(byte[] bytes)
        {
            var doc = JsonNode.Parse(bytes)!.AsObject();
            var records = doc.Select(kv => kv.Value).OfType<JsonArray>().Single();
            var canonicalRecords = Canonical(records);
            records.Clear();
            return doc.ToJsonString() + "\n" + canonicalRecords;
        }

        Assert.Equal(
            Normalize(await BytesAsync(Postgres, NestedOrder(), "json")),
            Normalize(await BytesAsync(MariaDb, NestedOrder(), "json"))
        );
    }

    [Fact]
    public async Task Xlsx_SameCells()
    {
        if (!await BothAvailableAsync())
            return;

        static string[] Rows(byte[] bytes)
        {
            using var workbook = new XLWorkbook(new MemoryStream(bytes));
            return workbook
                .Worksheets.SelectMany(ws =>
                    ws.RowsUsed()
                        .Select(r => ws.Name + "|" + string.Join("|", r.CellsUsed().Select(c => c.GetString())))
                )
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

        Assert.Equal(
            Rows(await BytesAsync(Postgres, NestedOrder(), "xlsx")),
            Rows(await BytesAsync(MariaDb, NestedOrder(), "xlsx"))
        );
    }

    [Fact]
    public async Task LegacyNestedGroups_SameTypedValues()
    {
        if (!await BothAvailableAsync())
            return;

        var cfg = new ExportMappingConfig(
            "export_order",
            [
                new ExportMappingField("id", "id", true),
                new ExportMappingField("placed_on", "placedOn", true),
                new ExportMappingField("total", "total", true),
            ],
            [],
            [
                new ExportMappingNestedGroup(
                    "customer",
                    "export_customer",
                    "id",
                    "customer_id",
                    true,
                    "object",
                    [
                        new ExportMappingNestedField("name", "name", true),
                        new ExportMappingNestedField("vip", "vip", true),
                    ],
                    []
                ),
            ]
        );

        async Task<string> RunAsync(IDataSourceProvider provider) =>
            Canonical(
                await DynamicExportService.ExecuteNestedJsonQueryAsync(
                    provider,
                    ConfigFor(provider),
                    cfg,
                    CancellationToken.None,
                    gdprDenylist: new HashSet<string>()
                )
            );

        var postgres = await RunAsync(Postgres);
        Assert.Contains("\"vip\":true", postgres);
        Assert.Equal(postgres, await RunAsync(MariaDb));
    }

    [Fact]
    public async Task MissingTable_FailsOnBoth()
    {
        if (!await BothAvailableAsync())
            return;

        foreach (var provider in new[] { Postgres, MariaDb })
            await Assert.ThrowsAsync<DataSourceQueryException>(() =>
                DynamicExportService.ExecuteExportNodeQueryAsync(
                    provider,
                    ConfigFor(provider),
                    "no_such_table",
                    FlatOrder(),
                    CancellationToken.None,
                    gdprDenylist: new HashSet<string>()
                )
            );
    }

    [Fact]
    public async Task InvalidCredentials_FailOnBoth()
    {
        if (!await BothAvailableAsync())
            return;

        foreach (var provider in new[] { Postgres, MariaDb })
        {
            var result = await provider.TestConnectionAsync(
                ConfigFor(provider) with
                {
                    Password = "wrong-pw",
                },
                CancellationToken.None
            );
            Assert.False(result.Success);
            Assert.DoesNotContain("wrong-pw", result.Error);
        }
    }

    [Fact]
    public async Task Timeout_AbortsOnBoth()
    {
        if (!await BothAvailableAsync())
            return;

        await Assert.ThrowsAnyAsync<Exception>(() =>
            Postgres.ExecuteNativeAsync(
                ErpTestFixture.Config,
                new NativeSqlQuery("SELECT pg_sleep(5)", CommandTimeoutSeconds: 1),
                CancellationToken.None
            )
        );
        await Assert.ThrowsAnyAsync<Exception>(() =>
            MariaDb.ExecuteNativeAsync(
                MariaDbTestFixture.Config,
                new NativeSqlQuery("SELECT SLEEP(5)", CommandTimeoutSeconds: 1),
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task BooleanScalar_IsTextOfTheBackendsOwnRepresentation()
    {
        if (!await BothAvailableAsync())
            return;

        var root = Root(
            Scalar("id", "id"),
            Node("customer", ExportNodeKind.Object, "export_customer", "id", "customer_id", null, Scalar("vip", "vip"))
        );

        string Vip(List<JsonObject> records) =>
            records.Single(r => r["id"]!.GetValue<string>() == "100")["customer"]!["vip"]!.GetValue<string>();

        Assert.Equal("true", Vip(await RecordsAsync(Postgres, root)));
        Assert.Equal("1", Vip(await RecordsAsync(MariaDb, root)));
    }
}
