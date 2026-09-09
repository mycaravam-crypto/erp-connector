using Connector.Core.DynamicExport;
using Connector.Infrastructure;

namespace Connector.Integration.Tests;

/// <summary>
/// Real-Postgres coverage for <see cref="DynamicExportService.ExecuteQueryAsync"/> — the flat legacy
/// CSV/Excel/JSON query path. Previously untested against a live database (unlike its nested-JSON sibling,
/// <see cref="DynamicExportServiceNestedJsonPostgresTests"/>); added alongside the security-review SR-08 fix
/// (GDPR denylist enforcement moved from a post-query, output-key-only strip to excluding a denylisted
/// SourceName/SourceField from the SELECT list itself) so that fix has real query-level coverage, not just
/// unit-level coverage of C#-side serialization.
///
/// Requires the local test fixture: <c>docker-compose --profile test up -d testdb</c> (see testdb/init.sql).
/// If the fixture isn't running, every test in this class no-ops rather than failing, matching every other
/// Postgres-backed test in this project (this repo's xunit version, 2.9.2, predates <c>Assert.Skip</c>).
/// </summary>
public sealed class DynamicExportServiceFlatQueryPostgresTests
{
    private const string AcmeItemId = "11111111-1111-1111-1111-111111111111"; // masterdata row → Acme

    [Fact]
    public async Task ExecuteQueryAsync_EnabledFields_ReturnsRenamedColumns()
    {
        await using var conn = await ErpTestFixture.TryOpenAsync();
        if (conn is null)
            return;

        var cfg = new ExportMappingConfig(
            "masterdata",
            Fields: [new ExportMappingField("article_name", "articleName", true)],
            Relations: []
        );

        var results = await DynamicExportService.ExecuteQueryAsync(conn, cfg, CancellationToken.None);

        var row = results.Single(r => r["articleName"] == "Compressor Unit CU-200");
        Assert.NotNull(row);
    }

    // Security-review finding SR-08: "technician_name" is denylisted by default
    // (DynamicExportService.GdprDeniedFields) but this mapping renames it to a TargetName that isn't
    // itself denylisted — the old post-query strip (which removed dictionary keys matching the denylist
    // verbatim) would have missed this; the field must never reach the SELECT list at all.
    [Fact]
    public async Task ExecuteQueryAsync_GdprDeniedField_ExcludedEvenWhenRenamed()
    {
        await using var conn = await ErpTestFixture.TryOpenAsync();
        if (conn is null)
            return;

        var cfg = new ExportMappingConfig(
            "systemconfiguration",
            Fields:
            [
                new ExportMappingField("id", "id", true),
                new ExportMappingField("technician_name", "assignedTech", true),
            ],
            Relations: []
        );

        var results = await DynamicExportService.ExecuteQueryAsync(conn, cfg, CancellationToken.None);

        var row = results.Single(r => r["id"] == "44444444-4444-4444-4444-444444444444");
        Assert.False(row.ContainsKey("assignedTech"));
    }
}
