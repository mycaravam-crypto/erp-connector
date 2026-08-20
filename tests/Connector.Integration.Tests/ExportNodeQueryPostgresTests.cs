using System.Text.Json.Nodes;
using Connector.Core.DynamicExport;
using Connector.Infrastructure;
using Npgsql;

namespace Connector.Integration.Tests;

/// <summary>
/// Real-Postgres coverage for <see cref="DynamicExportService.ExecuteExportNodeQueryAsync"/> — the Phase 14
/// generic tree query builder (<see cref="DynamicExportService"/>'s ExportNode-tree region). Same fixture,
/// same "no-op if the fixture isn't running" convention as <see cref="DynamicExportServiceNestedJsonPostgresTests"/>
/// (this repo's xunit 2.9.2 predates <c>Assert.Skip</c>). Requires
/// <c>docker-compose --profile test up -d testdb</c> — see testdb/init.sql for the seed data.
/// </summary>
public sealed class ExportNodeQueryPostgresTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=erp_testdb;Username=erp_test;Password=erp_test_pw;Timeout=2";

    // Seeded in testdb/init.sql: Acme Industrial has 2 addresses, Northbridge Sensors has 0.
    private const string AcmeManufacturerId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string AcmeItemId = "11111111-1111-1111-1111-111111111111"; // masterdata row → Acme
    private const string NorthbridgeItemId = "33333333-3333-3333-3333-333333333333"; // masterdata row → Northbridge

    private static async Task<NpgsqlConnection?> TryOpenAsync()
    {
        try
        {
            var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            return conn;
        }
        catch
        {
            return null;
        }
    }

    [Fact]
    public async Task ExecuteExportNodeQueryAsync_ScalarFieldAtRoot_ReturnsPlainColumn()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = MakeRoot(ScalarField("itemId", "id"));

        var results = await DynamicExportService.ExecuteExportNodeQueryAsync(
            conn,
            "masterdata",
            root,
            CancellationToken.None
        );

        var row = results.Single(r => r["itemId"]!.GetValue<string>() == AcmeItemId);
        Assert.NotNull(row);
    }

    [Fact]
    public async Task ExecuteExportNodeQueryAsync_ObjectKindNode_EmbedsSingleNestedObject()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = MakeRoot(
            ScalarField("itemId", "id"),
            Node(
                "manufacturer",
                ExportNodeKind.Object,
                "manufacturer",
                "id",
                "manufacturer_id",
                children: [ScalarField("name", "name"), ScalarField("contactEmail", "contact_email")]
            )
        );

        var results = await DynamicExportService.ExecuteExportNodeQueryAsync(
            conn,
            "masterdata",
            root,
            CancellationToken.None
        );

        var row = results.Single(r => r["itemId"]!.GetValue<string>() == AcmeItemId);
        var manufacturer = row["manufacturer"]!.AsObject();
        Assert.Equal("Acme Industrial", manufacturer["name"]!.GetValue<string>());
        Assert.Equal("info@acme-industrial.example", manufacturer["contactEmail"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteExportNodeQueryAsync_ArrayKindNode_EmbedsArrayOfObjects()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = MakeRoot(
            ScalarField("manufacturerId", "id"),
            Node(
                "addresses",
                ExportNodeKind.Array,
                "manufacturer_address",
                "manufacturer_id",
                "id",
                children: [ScalarField("addressType", "address_type"), ScalarField("city", "city")]
            )
        );

        var results = await DynamicExportService.ExecuteExportNodeQueryAsync(
            conn,
            "manufacturer",
            root,
            CancellationToken.None
        );

        var row = results.Single(r => r["manufacturerId"]!.GetValue<string>() == AcmeManufacturerId);
        var addresses = row["addresses"]!.AsArray();
        Assert.Equal(2, addresses.Count);
        Assert.Contains(addresses, a => a!["city"]!.GetValue<string>() == "Austin");
    }

    [Fact]
    public async Task ExecuteExportNodeQueryAsync_ThreeLevelNesting_ArrayNestsUnderObjectKey()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        // masterdata (root) -> manufacturer (object) -> addresses (array): the concrete 3-level walkthrough
        // PHASE-14-PLAN.md's end-to-end verification calls for.
        var root = MakeRoot(
            ScalarField("itemId", "id"),
            Node(
                "manufacturer",
                ExportNodeKind.Object,
                "manufacturer",
                "id",
                "manufacturer_id",
                children:
                [
                    ScalarField("name", "name"),
                    Node(
                        "addresses",
                        ExportNodeKind.Array,
                        "manufacturer_address",
                        "manufacturer_id",
                        "id",
                        children: [ScalarField("city", "city")]
                    ),
                ]
            )
        );

        var results = await DynamicExportService.ExecuteExportNodeQueryAsync(
            conn,
            "masterdata",
            root,
            CancellationToken.None
        );

        var row = results.Single(r => r["itemId"]!.GetValue<string>() == AcmeItemId);
        var manufacturer = row["manufacturer"]!.AsObject();
        Assert.Equal("Acme Industrial", manufacturer["name"]!.GetValue<string>());
        Assert.False(row.ContainsKey("addresses")); // nested inside manufacturer, not flat at row top level
        Assert.Equal(2, manufacturer["addresses"]!.AsArray().Count);
    }

    [Fact]
    public async Task ExecuteExportNodeQueryAsync_ZeroMatchingRelatedRows_YieldsEmptyArrayNotNull()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = MakeRoot(
            ScalarField("itemId", "id"),
            Node(
                "manufacturer",
                ExportNodeKind.Object,
                "manufacturer",
                "id",
                "manufacturer_id",
                children:
                [
                    Node(
                        "addresses",
                        ExportNodeKind.Array,
                        "manufacturer_address",
                        "manufacturer_id",
                        "id",
                        children: [ScalarField("city", "city")]
                    ),
                ]
            )
        );

        var results = await DynamicExportService.ExecuteExportNodeQueryAsync(
            conn,
            "masterdata",
            root,
            CancellationToken.None
        );

        var row = results.Single(r => r["itemId"]!.GetValue<string>() == NorthbridgeItemId);
        var addresses = row["manufacturer"]!["addresses"];
        Assert.NotNull(addresses);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, addresses!.GetValueKind());
        Assert.Empty(addresses.AsArray());
    }

    [Fact]
    public async Task ExecuteExportNodeQueryAsync_DisabledNodeAndDisabledField_AreExcluded()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = MakeRoot(
            ScalarField("itemId", "id"),
            Node(
                "manufacturer",
                ExportNodeKind.Object,
                "manufacturer",
                "id",
                "manufacturer_id",
                children: [ScalarField("name", "name"), ScalarField("contactEmail", "contact_email", enabled: false)]
            ),
            Node(
                "disabledGroup",
                ExportNodeKind.Object,
                "manufacturer",
                "id",
                "manufacturer_id",
                children: [],
                enabled: false
            )
        );

        var results = await DynamicExportService.ExecuteExportNodeQueryAsync(
            conn,
            "masterdata",
            root,
            CancellationToken.None
        );

        var row = results.Single(r => r["itemId"]!.GetValue<string>() == AcmeItemId);
        Assert.False(row.ContainsKey("disabledGroup"));
        var manufacturer = row["manufacturer"]!.AsObject();
        Assert.True(manufacturer.ContainsKey("name"));
        Assert.False(manufacturer.ContainsKey("contactEmail"));
    }

    [Fact]
    public async Task ExecuteExportNodeQueryAsync_GdprDeniedField_StrippedAtNestedDepth()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = MakeRoot(
            ScalarField("itemId", "id"),
            Node(
                "manufacturer",
                ExportNodeKind.Object,
                "manufacturer",
                "id",
                "manufacturer_id",
                // Not itself GDPR-denied; exported under a denylisted target key to prove stripping
                // matches by OUTPUT key name at every depth, same as the legacy path's contract.
                children: [ScalarField("contact_email", "contact_email")]
            )
        );
        var denylist = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "contact_email" };

        var results = await DynamicExportService.ExecuteExportNodeQueryAsync(
            conn,
            "masterdata",
            root,
            CancellationToken.None,
            gdprDenylist: denylist
        );

        var row = results.Single(r => r["itemId"]!.GetValue<string>() == AcmeItemId);
        Assert.False(row["manufacturer"]!.AsObject().ContainsKey("contact_email"));
    }

    [Fact]
    public async Task ExecuteExportNodeQueryAsync_FilterFragment_ScopesToNodeTable()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = MakeRoot(
            ScalarField("manufacturerId", "id"),
            Node(
                "addresses",
                ExportNodeKind.Array,
                "manufacturer_address",
                "manufacturer_id",
                "id",
                filter: "city = 'Austin'",
                children: [ScalarField("city", "city")]
            )
        );

        var results = await DynamicExportService.ExecuteExportNodeQueryAsync(
            conn,
            "manufacturer",
            root,
            CancellationToken.None
        );

        var row = results.Single(r => r["manufacturerId"]!.GetValue<string>() == AcmeManufacturerId);
        var addresses = row["addresses"]!.AsArray();
        Assert.Single(addresses); // Acme has 2 addresses total, only 1 in Austin
        Assert.Equal("Austin", addresses[0]!["city"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteExportNodeQueryAsync_RootFilter_ScopesRootRows()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = MakeRoot(ScalarField("itemId", "id")) with { Filter = "manufacturer = 'Northbridge Sensors'" };

        var results = await DynamicExportService.ExecuteExportNodeQueryAsync(
            conn,
            "masterdata",
            root,
            CancellationToken.None
        );

        Assert.Single(results);
        Assert.Equal(NorthbridgeItemId, results[0]["itemId"]!.GetValue<string>());
    }

    [Fact]
    public async Task BuildExportNodeAsync_CsvFormat_ThreeLevelTreeFlattensWithJoinedColumn()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = MakeRoot(
            ScalarField("manufacturerId", "id"),
            Node(
                "addresses",
                ExportNodeKind.Array,
                "manufacturer_address",
                "manufacturer_id",
                "id",
                children: [ScalarField("city", "city")]
            )
        );

        var result = await DynamicExportService.BuildExportNodeAsync(
            conn,
            "manufacturer",
            root,
            "csv",
            "v1",
            DateTimeOffset.UtcNow,
            CancellationToken.None
        );

        var text = System.Text.Encoding.UTF8.GetString(result.Bytes);
        Assert.Contains("addresses.city", text);
        // Acme's two seeded addresses (San Jose, Austin) both land in the joined column; json_agg order
        // isn't guaranteed, so assert both are present rather than a fixed ordering.
        Assert.Contains("San Jose", text);
        Assert.Contains("Austin", text);
        Assert.Equal("csv", result.Extension);
    }

    [Fact]
    public async Task BuildExportNodeAsync_ExcelFormat_ThreeLevelTreeFlattensIntoWorksheet()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = MakeRoot(
            ScalarField("itemId", "id"),
            Node(
                "manufacturer",
                ExportNodeKind.Object,
                "manufacturer",
                "id",
                "manufacturer_id",
                children:
                [
                    ScalarField("name", "name"),
                    Node(
                        "addresses",
                        ExportNodeKind.Array,
                        "manufacturer_address",
                        "manufacturer_id",
                        "id",
                        children: [ScalarField("city", "city")]
                    ),
                ]
            )
        );

        var result = await DynamicExportService.BuildExportNodeAsync(
            conn,
            "masterdata",
            root,
            "xlsx",
            "v1",
            DateTimeOffset.UtcNow,
            CancellationToken.None
        );

        Assert.Equal("xlsx", result.Extension);
        using var ms = new MemoryStream(result.Bytes);
        using var wb = new ClosedXML.Excel.XLWorkbook(ms);
        var ws = wb.Worksheet(1);
        // Row 1 = metadata, row 2 = headers ("itemId", "manufacturer.name", "manufacturer.addresses.city").
        Assert.Equal("manufacturer.addresses.city", ws.Cell(2, 3).GetString());
        var acmeRowIdx = Enumerable
            .Range(3, ws.LastRowUsed()!.RowNumber() - 2)
            .First(r => ws.Cell(r, 1).GetString() == AcmeItemId);
        Assert.Contains("San Jose", ws.Cell(acmeRowIdx, 3).GetString());
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static ExportNode MakeRoot(params ExportNode[] children) =>
        new("root", ExportNodeKind.Root, null, null, null, null, null, null, children, true);

    private static ExportNode ScalarField(string targetKey, string sourceField, bool enabled = true) =>
        new(targetKey, ExportNodeKind.ScalarField, sourceField, null, null, null, null, null, [], enabled);

    private static ExportNode Node(
        string targetKey,
        string kind,
        string relatedTable,
        string joinKey,
        string sourceJoinKey,
        ExportNode[] children,
        string? filter = null,
        bool enabled = true
    ) => new(targetKey, kind, null, relatedTable, joinKey, sourceJoinKey, filter, null, children, enabled);
}
