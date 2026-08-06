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

    [Fact]
    public void DeserializeConfig_NoNestedGroupsOrWrapper_BackfilledToEmptyAndNull()
    {
        // Every mapping saved before this feature shipped has no NestedGroups/JsonWrapper
        // property at all — must backfill to [] / null, never crash on missing properties.
        var config = ExportMappingJson.DeserializeConfig(LegacyConfigJson);

        Assert.NotNull(config!.NestedGroups);
        Assert.Empty(config.NestedGroups!);
        Assert.Null(config.JsonWrapper);
    }

    [Fact]
    public void DeserializeConfig_NestedGroupMissingChildrenAndFields_BackfilledRecursivelyThreeLevelsDeep()
    {
        const string json = """
            {
                "SourceTable": "masterdata",
                "Fields": [],
                "Relations": [],
                "NestedGroups": [
                    {
                        "TargetKey": "manufacturer",
                        "RelatedTable": "manufacturer",
                        "JoinKey": "id",
                        "SourceJoinKey": "manufacturer_id",
                        "Enabled": true,
                        "Kind": "object",
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

        var config = ExportMappingJson.DeserializeConfig(json);

        var manufacturer = Assert.Single(config!.NestedGroups!);
        Assert.NotNull(manufacturer.Fields);
        Assert.Empty(manufacturer.Fields);

        var addresses = Assert.Single(manufacturer.Children);
        Assert.NotNull(addresses.Fields);
        Assert.Empty(addresses.Fields);

        var tags = Assert.Single(addresses.Children);
        Assert.NotNull(tags.Fields);
        Assert.Empty(tags.Fields);
        Assert.NotNull(tags.Children);
        Assert.Empty(tags.Children);
    }

    [Fact]
    public void DeserializeConfig_NestedGroupMissingKind_DefaultsToObject()
    {
        const string json = """
            {
                "SourceTable": "masterdata",
                "Fields": [],
                "Relations": [],
                "NestedGroups": [
                    { "TargetKey": "manufacturer", "RelatedTable": "manufacturer", "JoinKey": "id", "SourceJoinKey": "manufacturer_id", "Enabled": true }
                ]
            }
            """;

        var config = ExportMappingJson.DeserializeConfig(json);

        Assert.Equal("object", Assert.Single(config!.NestedGroups!).Kind);
    }

    [Fact]
    public void DeserializeConfig_JsonWrapperPresentWithoutMetadataFields_BackfilledToEmpty()
    {
        const string json = """
            {
                "SourceTable": "masterdata",
                "Fields": [],
                "Relations": [],
                "JsonWrapper": { "RootKey": "masterData", "ItemsKey": "items", "MetadataKey": "metadata" }
            }
            """;

        var config = ExportMappingJson.DeserializeConfig(json);

        Assert.NotNull(config!.JsonWrapper);
        Assert.NotNull(config.JsonWrapper!.MetadataFields);
        Assert.Empty(config.JsonWrapper.MetadataFields);
    }

    [Fact]
    public void DeserializePresets_NestedGroupMissingChildren_BackfilledToEmptyArray()
    {
        const string json = """
            {
                "SourceTable": "masterdata",
                "Fields": [],
                "Relations": [],
                "NestedGroups": [
                    { "TargetKey": "manufacturer", "RelatedTable": "manufacturer", "JoinKey": "id", "SourceJoinKey": "manufacturer_id", "Enabled": true, "Kind": "object" }
                ]
            }
            """;
        var wrapped = $$"""{ "My Preset": {{json}} }""";

        var presets = ExportMappingJson.DeserializePresets(wrapped);

        var group = Assert.Single(presets["My Preset"].NestedGroups!);
        Assert.NotNull(group.Children);
        Assert.Empty(group.Children);
    }
}
