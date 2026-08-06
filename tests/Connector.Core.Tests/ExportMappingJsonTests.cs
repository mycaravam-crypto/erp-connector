using Connector.Core.DynamicExport;

namespace Connector.Core.Tests;

/// <summary>
/// Guards against a regression where AppSetting JSON saved before relations gained a
/// <c>Fields</c> list deserializes with <c>Fields == null</c> (System.Text.Json leaves a
/// missing array property null, not empty) and crashes every consumer that iterates it.
/// </summary>
public sealed class ExportMappingJsonTests
{
    private const string LegacyConfigJson = """
        {
            "SourceTable": "masterdata",
            "Fields": [{ "SourceName": "id", "TargetName": "id", "Enabled": true }],
            "Relations": [
                {
                    "RelatedTable": "systemconfiguration",
                    "JoinKey": "article_id",
                    "SourceJoinKey": "id",
                    "TargetField": "test",
                    "Enabled": true,
                    "FlattenStrategy": "string_join",
                    "StrategyOptions": { "SourceField": "serial", "Delimiter": ", " }
                }
            ]
        }
        """;

    [Fact]
    public void DeserializeConfig_LegacyRelationShape_FieldsIsEmptyNotNull()
    {
        var config = ExportMappingJson.DeserializeConfig(LegacyConfigJson);

        Assert.NotNull(config);
        var relation = Assert.Single(config!.Relations);
        Assert.NotNull(relation.Fields);
        Assert.Empty(relation.Fields);
    }

    [Fact]
    public void DeserializeConfig_LegacyRelationShape_DelimiterAndFlattenStrategyBackfilled()
    {
        // Legacy shape has no relation-level Delimiter or FlattenStrategy property at all —
        // System.Text.Json would otherwise leave both null, and DynamicExportService.ExecuteQueryAsync
        // dereferences Delimiter unconditionally for every enabled relation.
        const string json = """
            {
                "SourceTable": "masterdata",
                "Fields": [],
                "Relations": [
                    { "RelatedTable": "systemconfiguration", "JoinKey": "article_id", "SourceJoinKey": "id", "Enabled": true }
                ]
            }
            """;

        var config = ExportMappingJson.DeserializeConfig(json);

        var relation = Assert.Single(config!.Relations);
        Assert.NotNull(relation.Delimiter);
        Assert.NotNull(relation.FlattenStrategy);
    }

    [Fact]
    public void DeserializeConfig_CurrentShape_FieldsPassThroughUnchanged()
    {
        const string json = """
            {
                "SourceTable": "masterdata",
                "Fields": [],
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
                ]
            }
            """;

        var config = ExportMappingJson.DeserializeConfig(json);

        var relation = Assert.Single(config!.Relations);
        var field = Assert.Single(relation.Fields);
        Assert.Equal("serial", field.SourceField);
        Assert.Equal("serial_number", field.TargetField);
    }

    [Fact]
    public void DeserializePresets_LegacyRelationShape_FieldsIsEmptyNotNull()
    {
        var json = $$"""{ "My Preset": {{LegacyConfigJson}} }""";

        var presets = ExportMappingJson.DeserializePresets(json);

        var relation = Assert.Single(presets["My Preset"].Relations);
        Assert.NotNull(relation.Fields);
        Assert.Empty(relation.Fields);
    }
}
