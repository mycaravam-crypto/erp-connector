using System.Text.Json;

namespace Connector.Core.DynamicExport;

/// <summary>Discriminator values for <see cref="ExportNode.Kind"/>. A plain string, matching this
/// codebase's existing convention (see <see cref="ExportMappingNestedGroup.Kind"/>) rather than a
/// C# enum + <c>JsonStringEnumConverter</c>.</summary>
public static class ExportNodeKind
{
    /// <summary>The tree's entry point — one per <c>ExportDefinitionEntity</c>, rooted at its <c>RootTable</c>.</summary>
    public const string Root = "root";

    /// <summary>A plain column pulled from the node's own table and exported as a value (leaf, no children).</summary>
    public const string ScalarField = "scalar-field";

    /// <summary>A single embedded object (N:1 lookup join) — same shape as <see cref="ExportMappingNestedGroup"/>'s <c>"object"</c>.</summary>
    public const string Object = "object";

    /// <summary>An array of embedded objects (1:N join) — same shape as <see cref="ExportMappingNestedGroup"/>'s <c>"array"</c>.</summary>
    public const string Array = "array";
}

/// <summary>Discriminator values for <see cref="FieldMapping.Transform"/>.</summary>
public static class FieldTransform
{
    public const string None = "none";
    public const string Uppercase = "uppercase";
    public const string Lowercase = "lowercase";
    public const string Trim = "trim";
    public const string DateFormat = "dateFormat";
    public const string Constant = "constant";
}

/// <summary>Discriminator values for <see cref="FieldMapping.DataType"/>, used to coerce a scalar
/// value at read time (e.g. a SQL <c>::text</c>/<c>::numeric</c> cast) ahead of format-writer serialization.</summary>
public static class FieldDataType
{
    public const string String = "string";
    public const string Number = "number";
    public const string Boolean = "boolean";
    public const string Date = "date";
}

/// <summary>Value-shaping for one <see cref="ExportNode"/> of kind <see cref="ExportNodeKind.ScalarField"/>:
/// an optional transform applied to the raw value, a fallback for null/missing values, and the target
/// data type used for format-writer coercion.</summary>
public record FieldMapping(string? DefaultValue, string Transform, string? TransformArg, string DataType);

/// <summary>
/// One node in a recursive export tree. Generalizes <see cref="ExportMappingNestedGroup"/> (which only
/// covers "object"/"array" nesting for JSON output) to also represent the root and plain scalar columns,
/// so a single tree — walked by one query builder and honored by every format writer (CSV/Excel/JSON) —
/// replaces the old parallel <c>Fields</c>/<c>Relations</c>/<c>NestedGroups</c> shapes. <see cref="Children"/>
/// lets root/object/array nodes nest arbitrarily deep; a scalar-field node has no children.
/// </summary>
public record ExportNode(
    string TargetKey,
    string Kind,
    string? SourceField,
    string? RelatedTable,
    string? JoinKey,
    string? SourceJoinKey,
    string? Filter,
    FieldMapping? Mapping,
    ExportNode[] Children,
    bool Enabled
);

/// <summary>
/// Deserializes an <see cref="ExportNode"/> tree stored as JSON (on <c>ExportDefinitionEntity.RootNode</c>),
/// backfilling missing properties the same way <see cref="ExportMappingJson"/> does for the legacy mapping
/// shape: System.Text.Json leaves a missing property at its type default rather than the value an older
/// saved tree implied, which would otherwise crash every consumer that dereferences it. Every read of a
/// persisted <see cref="ExportNode"/> tree must go through this instead of a raw
/// <see cref="JsonSerializer.Deserialize{TValue}(string, JsonSerializerOptions?)"/> call.
/// </summary>
public static class ExportNodeJson
{
    public static ExportNode? Deserialize(string json)
    {
        var node = JsonSerializer.Deserialize<ExportNode>(json);
        return node is null ? null : Normalize(node);
    }

    public static string Serialize(ExportNode node) => JsonSerializer.Serialize(node);

    // Recurses into Children so a tree missing Kind/Children/Mapping properties at any depth gets
    // the same backfill, matching ExportMappingJson.NormalizeNestedGroup's recursive pattern.
    private static ExportNode Normalize(ExportNode node) =>
        node with
        {
            Kind = string.IsNullOrWhiteSpace(node.Kind) ? ExportNodeKind.Object : node.Kind,
            Children = (node.Children ?? []).Select(Normalize).ToArray(),
            Mapping = node.Mapping is null ? null : NormalizeMapping(node.Mapping),
        };

    private static FieldMapping NormalizeMapping(FieldMapping mapping) =>
        mapping with
        {
            Transform = string.IsNullOrWhiteSpace(mapping.Transform) ? FieldTransform.None : mapping.Transform,
            DataType = string.IsNullOrWhiteSpace(mapping.DataType) ? FieldDataType.String : mapping.DataType,
        };
}
