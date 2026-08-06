using System.Text.Json.Nodes;
using Connector.Core.DynamicExport;
using Connector.Infrastructure;
using Npgsql;

namespace Connector.Integration.Tests;

/// <summary>
/// Real-Postgres coverage for <see cref="DynamicExportService.ExecuteNestedJsonQueryAsync"/> — the recursive
/// json_build_object/json_agg SQL generation that backs nested JSON export. Unlike the rest of
/// <see cref="DynamicExportServiceTests"/>, these tests need an actual database because the thing under
/// test IS the generated SQL, not just C#-side serialization.
///
/// Requires the local test fixture: <c>docker-compose --profile test up -d testdb</c> (see testdb/init.sql
/// for the manufacturer/manufacturer_address seed data these tests query). If the fixture isn't running,
/// every test in this class no-ops rather than failing, since this repo's xunit version (2.9.2) predates
/// <c>Assert.Skip</c>.
/// </summary>
public sealed class DynamicExportServiceNestedJsonPostgresTests
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
    public async Task ExecuteNestedJsonQueryAsync_ObjectKindGroup_EmbedsSingleNestedObject()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var cfg = MakeConfig(
            fields: [new("id", "itemId", true)],
            nestedGroups:
            [
                new(
                    "manufacturer",
                    "manufacturer",
                    "id",
                    "manufacturer_id",
                    true,
                    "object",
                    [new("name", "name", true), new("contact_email", "contactEmail", true)],
                    []
                ),
            ]
        );

        var results = await DynamicExportService.ExecuteNestedJsonQueryAsync(conn, cfg, CancellationToken.None);

        var row = results.Single(r => r["itemId"]!.GetValue<string>() == AcmeItemId);
        var manufacturer = row["manufacturer"]!.AsObject();
        Assert.Equal("Acme Industrial", manufacturer["name"]!.GetValue<string>());
        Assert.Equal("info@acme-industrial.example", manufacturer["contactEmail"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteNestedJsonQueryAsync_ArrayKindGroup_EmbedsArrayOfObjects()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var cfg = MakeConfig(
            fields: [new("id", "manufacturerId", true)],
            nestedGroups:
            [
                new(
                    "addresses",
                    "manufacturer_address",
                    "manufacturer_id",
                    "id",
                    true,
                    "array",
                    [new("address_type", "addressType", true), new("city", "city", true)],
                    []
                ),
            ],
            sourceTable: "manufacturer"
        );

        var results = await DynamicExportService.ExecuteNestedJsonQueryAsync(conn, cfg, CancellationToken.None);

        var row = results.Single(r => r["manufacturerId"]!.GetValue<string>() == AcmeManufacturerId);
        var addresses = row["addresses"]!.AsArray();
        Assert.Equal(2, addresses.Count);
        Assert.Contains(addresses, a => a!["city"]!.GetValue<string>() == "Austin");
    }

    [Fact]
    public async Task ExecuteNestedJsonQueryAsync_TwoHopNesting_ChildGroupNestsUnderParentKey()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var cfg = MakeConfig(
            fields: [new("id", "itemId", true)],
            nestedGroups:
            [
                new(
                    "manufacturer",
                    "manufacturer",
                    "id",
                    "manufacturer_id",
                    true,
                    "object",
                    [new("name", "name", true)],
                    [
                        new(
                            "addresses",
                            "manufacturer_address",
                            "manufacturer_id",
                            "id", // matched against manufacturer.id via the "manufacturer" alias, not masterdata
                            true,
                            "array",
                            [new("address_type", "addressType", true)],
                            []
                        ),
                    ]
                ),
            ]
        );

        var results = await DynamicExportService.ExecuteNestedJsonQueryAsync(conn, cfg, CancellationToken.None);

        var row = results.Single(r => r["itemId"]!.GetValue<string>() == AcmeItemId);
        var manufacturer = row["manufacturer"]!.AsObject();
        Assert.Equal("Acme Industrial", manufacturer["name"]!.GetValue<string>());
        // The core new capability: addresses is nested INSIDE manufacturer, not flat at the row's top level.
        Assert.False(row.ContainsKey("addresses"));
        var addresses = manufacturer["addresses"]!.AsArray();
        Assert.Equal(2, addresses.Count);
    }

    [Fact]
    public async Task ExecuteNestedJsonQueryAsync_DisabledGroupAndDisabledField_AreExcluded()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var cfg = MakeConfig(
            fields: [new("id", "itemId", true)],
            nestedGroups:
            [
                new(
                    "manufacturer",
                    "manufacturer",
                    "id",
                    "manufacturer_id",
                    true,
                    "object",
                    [new("name", "name", true), new("contact_email", "contactEmail", false)],
                    []
                ),
                new("disabledGroup", "manufacturer", "id", "manufacturer_id", false, "object", [], []),
            ]
        );

        var results = await DynamicExportService.ExecuteNestedJsonQueryAsync(conn, cfg, CancellationToken.None);

        var row = results.Single(r => r["itemId"]!.GetValue<string>() == AcmeItemId);
        Assert.False(row.ContainsKey("disabledGroup"));
        var manufacturer = row["manufacturer"]!.AsObject();
        Assert.True(manufacturer.ContainsKey("name"));
        Assert.False(manufacturer.ContainsKey("contactEmail"));
    }

    [Fact]
    public async Task ExecuteNestedJsonQueryAsync_ZeroMatchingRelatedRows_YieldsEmptyArrayNotNull()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var cfg = MakeConfig(
            fields: [new("id", "itemId", true)],
            nestedGroups:
            [
                new(
                    "manufacturer",
                    "manufacturer",
                    "id",
                    "manufacturer_id",
                    true,
                    "object",
                    [],
                    [
                        new(
                            "addresses",
                            "manufacturer_address",
                            "manufacturer_id",
                            "id",
                            true,
                            "array",
                            [new("city", "city", true)],
                            []
                        ),
                    ]
                ),
            ]
        );

        var results = await DynamicExportService.ExecuteNestedJsonQueryAsync(conn, cfg, CancellationToken.None);

        var row = results.Single(r => r["itemId"]!.GetValue<string>() == NorthbridgeItemId);
        var addresses = row["manufacturer"]!["addresses"];
        Assert.NotNull(addresses);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, addresses!.GetValueKind());
        Assert.Empty(addresses.AsArray());
    }

    [Fact]
    public async Task ExecuteNestedJsonQueryAsync_GdprDeniedField_StrippedFromNestedObject()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var cfg = MakeConfig(
            fields: [new("id", "itemId", true)],
            nestedGroups:
            [
                new(
                    "manufacturer",
                    "manufacturer",
                    "id",
                    "manufacturer_id",
                    true,
                    "object",
                    // "contact_email" is not itself denied, but exported under a denylisted target key
                    // to prove stripping matches by OUTPUT key name, same as the flat path's behavior.
                    [new("contact_email", "contact_email", true)],
                    []
                ),
            ]
        );
        var denylist = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "contact_email" };

        var results = await DynamicExportService.ExecuteNestedJsonQueryAsync(
            conn,
            cfg,
            CancellationToken.None,
            gdprDenylist: denylist
        );

        var row = results.Single(r => r["itemId"]!.GetValue<string>() == AcmeItemId);
        Assert.False(row["manufacturer"]!.AsObject().ContainsKey("contact_email"));
    }

    [Fact]
    public async Task ExecuteNestedJsonQueryAsync_TargetKeyWithApostrophe_EscapedSafely()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var cfg = MakeConfig(
            fields: [new("id", "item's id", true)], // apostrophe in a JSON key literal
            nestedGroups: []
        );

        var results = await DynamicExportService.ExecuteNestedJsonQueryAsync(conn, cfg, CancellationToken.None);

        var row = results.Single(r => r["item's id"]!.GetValue<string>() == AcmeItemId);
        Assert.NotNull(row);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ExportMappingConfig MakeConfig(
        ExportMappingField[] fields,
        ExportMappingNestedGroup[] nestedGroups,
        string sourceTable = "masterdata"
    ) => new(sourceTable, fields, [], nestedGroups);
}
