using Connector.Core.DynamicExport;

namespace Connector.Core.Tests;

/// <summary>
/// Guards <see cref="ExportNodeJson"/>'s missing-property backfill — a persisted <see cref="ExportNode"/>
/// tree saved before a later field was added must still deserialize without leaving <c>Kind</c>/<c>Children</c>
/// at System.Text.Json's type default (<see langword="null"/> for the array, empty string for <c>Kind</c>),
/// which would otherwise crash every consumer that iterates/switches on them. Mirrors
/// <see cref="ExportMappingJsonTests"/>'s coverage style for the older <see cref="ExportMappingNestedGroup"/> shape.
/// </summary>
public sealed class ExportNodeTests
{
    [Fact]
    public void Deserialize_MissingKindAndChildren_BackfilledRecursively()
    {
        const string json = """
            {
                "TargetKey": "root",
                "Children": [
                    {
                        "TargetKey": "manufacturer",
                        "RelatedTable": "manufacturer",
                        "Children": [
                            { "TargetKey": "name", "Kind": "scalar-field", "SourceField": "name" }
                        ]
                    }
                ]
            }
            """;

        var node = ExportNodeJson.Deserialize(json);

        Assert.NotNull(node);
        Assert.Equal(ExportNodeKind.Object, node!.Kind); // missing Kind backfills to "object"

        var manufacturer = Assert.Single(node.Children);
        Assert.Equal(ExportNodeKind.Object, manufacturer.Kind);

        var name = Assert.Single(manufacturer.Children);
        Assert.Equal(ExportNodeKind.ScalarField, name.Kind);
        Assert.NotNull(name.Children);
        Assert.Empty(name.Children);
    }

    [Fact]
    public void Deserialize_MappingMissingTransformAndDataType_BackfilledToDefaults()
    {
        const string json = """
            {
                "TargetKey": "email",
                "Kind": "scalar-field",
                "SourceField": "email",
                "Mapping": { "DefaultValue": "unknown@example.com" }
            }
            """;

        var node = ExportNodeJson.Deserialize(json);

        Assert.NotNull(node!.Mapping);
        Assert.Equal(FieldTransform.None, node.Mapping!.Transform);
        Assert.Equal(FieldDataType.String, node.Mapping.DataType);
        Assert.Equal("unknown@example.com", node.Mapping.DefaultValue);
    }

    [Fact]
    public void Deserialize_NoMapping_StaysNull()
    {
        const string json = """{ "TargetKey": "id", "Kind": "scalar-field", "SourceField": "id" }""";

        var node = ExportNodeJson.Deserialize(json);

        Assert.Null(node!.Mapping);
    }

    [Fact]
    public void Deserialize_CurrentShape_PassesThroughUnchanged()
    {
        const string json = """
            {
                "TargetKey": "quantity",
                "Kind": "scalar-field",
                "SourceField": "qty",
                "Enabled": true,
                "Mapping": { "Transform": "uppercase", "DataType": "number" },
                "Children": []
            }
            """;

        var node = ExportNodeJson.Deserialize(json);

        Assert.Equal("qty", node!.SourceField);
        Assert.True(node.Enabled);
        Assert.Equal("uppercase", node.Mapping!.Transform);
        Assert.Equal("number", node.Mapping.DataType);
    }

    [Fact]
    public void Serialize_ThenDeserialize_PreservesTreeShape()
    {
        var node = new ExportNode(
            TargetKey: "root",
            Kind: ExportNodeKind.Root,
            SourceField: null,
            RelatedTable: null,
            JoinKey: null,
            SourceJoinKey: null,
            Filter: null,
            Mapping: null,
            Children: [new ExportNode("id", ExportNodeKind.ScalarField, "id", null, null, null, null, null, [], true)],
            Enabled: true
        );

        var roundTripped = ExportNodeJson.Deserialize(ExportNodeJson.Serialize(node));

        Assert.Equal(ExportNodeKind.Root, roundTripped!.Kind);
        var child = Assert.Single(roundTripped.Children);
        Assert.Equal("id", child.TargetKey);
        Assert.Equal(ExportNodeKind.ScalarField, child.Kind);
        Assert.True(child.Enabled);
    }
}
