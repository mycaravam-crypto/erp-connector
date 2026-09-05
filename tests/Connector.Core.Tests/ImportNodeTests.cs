using Connector.Core.DynamicImport;

namespace Connector.Core.Tests;

/// <summary>
/// Guards <see cref="ImportNodeJson"/>'s missing-property backfill — mirrors
/// <see cref="ExportNodeTests"/>'s coverage style for the export-side tree, since
/// <see cref="ImportNode"/> is deliberately shaped the same way (see its own doc comment for why it
/// isn't the same type).
/// </summary>
public sealed class ImportNodeTests
{
    [Fact]
    public void Deserialize_MissingKindAndOnMissingChildAndChildren_BackfilledRecursively()
    {
        const string json = """
            {
                "SourceKey": "root",
                "Children": [
                    {
                        "SourceKey": "serialNumbers",
                        "RelatedTable": "serial_number",
                        "Children": [
                            { "SourceKey": "status", "Kind": "scalar-field", "TargetColumn": "status" }
                        ]
                    }
                ]
            }
            """;

        var node = ImportNodeJson.Deserialize(json);

        Assert.NotNull(node);
        Assert.Equal(ImportNodeKind.Object, node!.Kind); // missing Kind backfills to "object"
        Assert.Equal(OnMissingChildPolicy.Reject, node.OnMissingChild); // missing OnMissingChild backfills to "reject"

        var child = Assert.Single(node.Children);
        Assert.Equal(ImportNodeKind.Object, child.Kind);
        Assert.Equal(OnMissingChildPolicy.Reject, child.OnMissingChild);

        var status = Assert.Single(child.Children);
        Assert.Equal(ImportNodeKind.ScalarField, status.Kind);
        Assert.NotNull(status.Children);
        Assert.Empty(status.Children);
    }

    [Fact]
    public void Deserialize_MappingMissingTransformAndDataType_BackfilledToDefaults()
    {
        const string json = """
            {
                "SourceKey": "confirmationStatus",
                "Kind": "scalar-field",
                "TargetColumn": "confirmation_status",
                "Mapping": { "DefaultValue": "pending" }
            }
            """;

        var node = ImportNodeJson.Deserialize(json);

        Assert.NotNull(node!.Mapping);
        Assert.Equal(Connector.Core.DynamicExport.FieldTransform.None, node.Mapping!.Transform);
        Assert.Equal(Connector.Core.DynamicExport.FieldDataType.String, node.Mapping.DataType);
        Assert.Equal("pending", node.Mapping.DefaultValue);
    }

    [Fact]
    public void Deserialize_NoMapping_StaysNull()
    {
        const string json = """{ "SourceKey": "id", "Kind": "scalar-field", "TargetColumn": "id" }""";

        var node = ImportNodeJson.Deserialize(json);

        Assert.Null(node!.Mapping);
    }

    [Fact]
    public void Serialize_ThenDeserialize_PreservesTreeShape()
    {
        var node = new ImportNode(
            SourceKey: "root",
            Kind: ImportNodeKind.Root,
            TargetColumn: null,
            RelatedTable: null,
            JoinKey: null,
            SourceJoinKey: null,
            OnMissingChild: OnMissingChildPolicy.Reject,
            Mapping: null,
            Children:
            [
                new ImportNode(
                    SourceKey: "status",
                    Kind: ImportNodeKind.ScalarField,
                    TargetColumn: "status",
                    RelatedTable: null,
                    JoinKey: null,
                    SourceJoinKey: null,
                    OnMissingChild: OnMissingChildPolicy.Reject,
                    Mapping: null,
                    Children: [],
                    Enabled: true
                ),
            ],
            Enabled: true
        );

        var roundTripped = ImportNodeJson.Deserialize(ImportNodeJson.Serialize(node));

        Assert.Equal(ImportNodeKind.Root, roundTripped!.Kind);
        var child = Assert.Single(roundTripped.Children);
        Assert.Equal("status", child.SourceKey);
        Assert.Equal(ImportNodeKind.ScalarField, child.Kind);
        Assert.True(child.Enabled);
    }
}
