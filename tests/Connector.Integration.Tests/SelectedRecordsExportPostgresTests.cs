using Connector.Core.DynamicExport;
using Connector.Infrastructure;
using Connector.Infrastructure.DataSources.PostgreSql;

namespace Connector.Integration.Tests;

/// <summary>Checks selected IDs are parameterized, intersect saved filters and preserve nested child relations.</summary>
public sealed class SelectedRecordsExportPostgresTests
{
    private static readonly PostgreSqlDataSourceProvider Provider = new();
    private const string FirstId = "11111111-1111-1111-1111-111111111111";
    private const string SecondId = "33333333-3333-3333-3333-333333333333";

    private static ExportNode Root(string? filter = null) =>
        new(
            "root",
            ExportNodeKind.Root,
            null,
            null,
            null,
            null,
            filter,
            null,
            [
                new ExportNode("id", ExportNodeKind.ScalarField, "id", null, null, null, null, null, [], true),
                new ExportNode(
                    "manufacturer",
                    ExportNodeKind.Object,
                    null,
                    "manufacturer",
                    "id",
                    "manufacturer_id",
                    null,
                    null,
                    [
                        new ExportNode(
                            "name",
                            ExportNodeKind.ScalarField,
                            "name",
                            null,
                            null,
                            null,
                            null,
                            null,
                            [],
                            true
                        ),
                    ],
                    true
                ),
            ],
            true
        );

    [Fact]
    public async Task SelectOneId_RetainsNestedObject()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;
        var rows = await DynamicExportService.ExecuteExportNodeQueryAsync(
            Provider,
            ErpTestFixture.Config,
            "masterdata",
            Root(),
            CancellationToken.None,
            selection: new ExportRecordSelection("id", [FirstId])
        );
        var row = Assert.Single(rows);
        Assert.Equal(FirstId, row["id"]!.GetValue<string>());
        Assert.Equal("Acme Industrial", row["manufacturer"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task SelectSeveralIds_IntersectsWithSavedRootFilter()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;
        var rows = await DynamicExportService.ExecuteExportNodeQueryAsync(
            Provider,
            ErpTestFixture.Config,
            "masterdata",
            Root($"id = '{FirstId}'"),
            CancellationToken.None,
            selection: new ExportRecordSelection("id", [FirstId, SecondId])
        );
        Assert.Equal(FirstId, Assert.Single(rows)["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task InjectedId_IsBoundAndMatchesNothing()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;
        var rows = await DynamicExportService.ExecuteExportNodeQueryAsync(
            Provider,
            ErpTestFixture.Config,
            "masterdata",
            Root(),
            CancellationToken.None,
            selection: new ExportRecordSelection("id", ["' OR 1=1 --"])
        );
        Assert.Empty(rows);
    }
}
