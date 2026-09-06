using Connector.Core.DynamicExport;
using Connector.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Connector.Integration.Tests;

/// <summary>
/// Coverage for <see cref="ExportDefinitionMigrator"/> against an in-memory SQLite
/// <see cref="ExportLogDbContext"/> — this only exercises AppSetting → ExportDefinition conversion logic,
/// not any live ERP/testdb connection, so unlike <see cref="DynamicExportServiceNestedJsonPostgresTests"/>
/// it needs no external fixture and never no-ops.
/// </summary>
public sealed class ExportDefinitionMigratorTests : SqliteDbContextTestBase
{
    private const string LegacyMappingJson = """
        {
            "SourceTable": "masterdata",
            "Fields": [{ "SourceName": "id", "TargetName": "id", "Enabled": true }],
            "Relations": [
                {
                    "RelatedTable": "systemconfiguration",
                    "JoinKey": "article_id",
                    "SourceJoinKey": "id",
                    "Enabled": true,
                    "FlattenStrategy": "string_join",
                    "Delimiter": ", ",
                    "Fields": [{ "SourceField": "serial", "TargetField": "serial_number", "Enabled": true }]
                }
            ],
            "NestedGroups": [
                {
                    "TargetKey": "manufacturer",
                    "RelatedTable": "manufacturer",
                    "JoinKey": "id",
                    "SourceJoinKey": "manufacturer_id",
                    "Enabled": true,
                    "Kind": "object",
                    "Fields": [{ "SourceField": "name", "TargetKey": "name", "Enabled": true }],
                    "Children": [
                        {
                            "TargetKey": "addresses",
                            "RelatedTable": "manufacturer_address",
                            "JoinKey": "manufacturer_id",
                            "SourceJoinKey": "id",
                            "Enabled": true,
                            "Kind": "array",
                            "Children": [
                                {
                                    "TargetKey": "tags",
                                    "RelatedTable": "address_tag",
                                    "JoinKey": "address_id",
                                    "SourceJoinKey": "id",
                                    "Enabled": true,
                                    "Kind": "array"
                                }
                            ]
                        }
                    ]
                }
            ]
        }
        """;

    private async Task SeedSettingAsync(string key, string rawJson)
    {
        Db.AppSettings.Add(new AppSettingEntity { Key = key, Value = rawJson });
        await Db.SaveChangesAsync();
    }

    [Fact]
    public async Task MigrateLegacyMappingsAsync_EmptyState_NoOp()
    {
        await ExportDefinitionMigrator.MigrateLegacyMappingsAsync(Db);

        Assert.Empty(Db.ExportDefinitions);
    }

    [Fact]
    public async Task MigrateLegacyMappingsAsync_SingleLegacyMapping_ConvertsFieldsRelationsAndNestedGroupsThreeLevelsDeep()
    {
        await SeedSettingAsync(SettingsKeys.ExportMapping, LegacyMappingJson);

        await ExportDefinitionMigrator.MigrateLegacyMappingsAsync(Db);

        var definition = Assert.Single(Db.ExportDefinitions);
        Assert.Equal("Legacy Export", definition.Name);
        Assert.Equal("masterdata", definition.RootTable);
        // An enabled NestedGroup was present — legacy nesting only ever rendered via the JSON path.
        Assert.Equal("json", definition.OutputFormat);

        var root = ExportNodeJson.Deserialize(definition.RootNode)!;
        Assert.Equal(ExportNodeKind.Root, root.Kind);

        var scalarField = Assert.Single(root.Children, c => c.Kind == ExportNodeKind.ScalarField);
        Assert.Equal("id", scalarField.SourceField);
        Assert.Equal("id", scalarField.TargetKey);

        var relationNode = Assert.Single(root.Children, c => c.TargetKey == "systemconfiguration");
        Assert.Equal(ExportNodeKind.Array, relationNode.Kind);
        var relationField = Assert.Single(relationNode.Children);
        Assert.Equal("serial", relationField.SourceField);
        Assert.Equal("serial_number", relationField.TargetKey);

        var manufacturer = Assert.Single(root.Children, c => c.TargetKey == "manufacturer");
        Assert.Equal(ExportNodeKind.Object, manufacturer.Kind);
        Assert.Contains(manufacturer.Children, c => c.TargetKey == "name" && c.Kind == ExportNodeKind.ScalarField);

        var addresses = Assert.Single(manufacturer.Children, c => c.TargetKey == "addresses");
        Assert.Equal(ExportNodeKind.Array, addresses.Kind);

        var tags = Assert.Single(addresses.Children, c => c.TargetKey == "tags");
        Assert.Equal(ExportNodeKind.Array, tags.Kind);
        Assert.Empty(tags.Children);
    }

    [Fact]
    public async Task MigrateLegacyMappingsAsync_Presets_EachBecomesSeparateDefinition()
    {
        const string presetsJson = """
            {
                "Preset A": { "SourceTable": "masterdata", "Fields": [], "Relations": [] },
                "Preset B": { "SourceTable": "systemconfiguration", "Fields": [], "Relations": [] }
            }
            """;
        await SeedSettingAsync(SettingsKeys.ExportPresets, presetsJson);

        await ExportDefinitionMigrator.MigrateLegacyMappingsAsync(Db);

        var names = await Db.ExportDefinitions.Select(d => d.Name).OrderBy(n => n).ToArrayAsync();
        Assert.Equal(["Preset A", "Preset B"], names);
    }

    [Fact]
    public async Task MigrateLegacyMappingsAsync_MappingAndPresetsBothPresent_ProducesOneDefinitionEach()
    {
        const string presetsJson =
            """{ "Preset A": { "SourceTable": "systemconfiguration", "Fields": [], "Relations": [] } }""";
        await SeedSettingAsync(SettingsKeys.ExportMapping, LegacyMappingJson);
        await SeedSettingAsync(SettingsKeys.ExportPresets, presetsJson);

        await ExportDefinitionMigrator.MigrateLegacyMappingsAsync(Db);

        var names = await Db.ExportDefinitions.Select(d => d.Name).OrderBy(n => n).ToArrayAsync();
        Assert.Equal(["Legacy Export", "Preset A"], names);
    }

    [Fact]
    public async Task MigrateLegacyMappingsAsync_RunTwice_DoesNotDuplicate()
    {
        await SeedSettingAsync(SettingsKeys.ExportMapping, LegacyMappingJson);

        await ExportDefinitionMigrator.MigrateLegacyMappingsAsync(Db);
        await ExportDefinitionMigrator.MigrateLegacyMappingsAsync(Db);

        Assert.Single(Db.ExportDefinitions);
    }

    [Fact]
    public async Task MigrateLegacyMappingsAsync_NoNestedGroups_UsesSchedulerConfigFormat()
    {
        const string flatMappingJson = """
            { "SourceTable": "masterdata", "Fields": [{ "SourceName": "id", "TargetName": "id", "Enabled": true }], "Relations": [] }
            """;
        const string schedulerJson = """{ "ScheduledTimeUtc": "06:00:00", "RetentionDays": 30, "Format": "csv" }""";
        await SeedSettingAsync(SettingsKeys.ExportMapping, flatMappingJson);
        await SeedSettingAsync(SettingsKeys.SchedulerConfig, schedulerJson);

        await ExportDefinitionMigrator.MigrateLegacyMappingsAsync(Db);

        var definition = Assert.Single(Db.ExportDefinitions);
        Assert.Equal("csv", definition.OutputFormat);
    }
}
