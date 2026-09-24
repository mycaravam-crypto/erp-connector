using Connector.Core.DynamicExport;
using Connector.Infrastructure;
using Connector.Infrastructure.DataSources.PostgreSql;

namespace Connector.Integration.Tests;

/// <summary>
/// Real-Postgres coverage for <see cref="DynamicExportService.ExecuteQueryAsync"/> — the flat legacy
/// CSV/Excel/JSON query path (the nested-JSON sibling is
/// <see cref="DynamicExportServiceNestedJsonPostgresTests"/>). Covers GDPR denylist enforcement at query
/// level: a denylisted SourceName/SourceField is excluded from the SELECT list itself.
///
/// Requires the local test fixture: <c>docker-compose --profile test up -d testdb</c> (see testdb/init.sql).
/// If the fixture isn't running, every test in this class no-ops rather than failing, matching every other
/// Postgres-backed test in this project (this repo's xunit version, 2.9.2, predates <c>Assert.Skip</c>).
/// </summary>
public sealed class DynamicExportServiceFlatQueryPostgresTests
{
    private static readonly PostgreSqlDataSourceProvider Provider = new();

    private const string AcmeItemId = "11111111-1111-1111-1111-111111111111"; // masterdata row → Acme

    [Fact]
    public async Task ExecuteQueryAsync_EnabledFields_ReturnsRenamedColumns()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        var cfg = new ExportMappingConfig(
            "masterdata",
            Fields:
            [
                new ExportMappingField("id", "id", true),
                new ExportMappingField("article_name", "articleName", true),
            ],
            Relations: []
        );

        var results = await DynamicExportService.ExecuteQueryAsync(
            Provider,
            ErpTestFixture.Config,
            cfg,
            CancellationToken.None
        );

        var row = results.Single(r => r["id"] == AcmeItemId);
        Assert.Equal("Compressor Unit CU-200", row["articleName"]);
    }

    // "technician_name" is denylisted by default (DynamicExportService.GdprDeniedFields) but this mapping
    // renames it to a TargetName that isn't itself denylisted — a strip by output key would miss it; the
    // field must never reach the SELECT list at all.
    [Fact]
    public async Task ExecuteQueryAsync_GdprDeniedField_ExcludedEvenWhenRenamed()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
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

        var results = await DynamicExportService.ExecuteQueryAsync(
            Provider,
            ErpTestFixture.Config,
            cfg,
            CancellationToken.None
        );

        var row = results.Single(r => r["id"] == "44444444-4444-4444-4444-444444444444");
        Assert.False(row.ContainsKey("assignedTech"));
    }
}
