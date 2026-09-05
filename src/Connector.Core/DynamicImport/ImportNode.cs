using System.Text.Json;
using Connector.Core.DynamicExport;

namespace Connector.Core.DynamicImport;

/// <summary>Discriminator values for <see cref="ImportNode.Kind"/> — same set as
/// <see cref="Connector.Core.DynamicExport.ExportNodeKind"/>, kept as its own type since
/// <see cref="ImportNode"/> intentionally doesn't merge into <see cref="ExportNode"/> (see that
/// record's doc comment for why).</summary>
public static class ImportNodeKind
{
    /// <summary>The tree's entry point — one per <c>ImportDefinitionEntity</c>, rooted at its <c>RootTable</c>.</summary>
    public const string Root = "root";

    /// <summary>A plain column written on the node's own table (leaf, no children).</summary>
    public const string ScalarField = "scalar-field";

    /// <summary>A single embedded object (N:1 lookup) — match-only, never created; see <see cref="OnMissingChildPolicy"/>.</summary>
    public const string Object = "object";

    /// <summary>An array of embedded objects (1:N) — the only child kind that may be created, per <see cref="OnMissingChildPolicy.Insert"/>.</summary>
    public const string Array = "array";
}

/// <summary>Discriminator values for <see cref="ImportNode.OnMissingChild"/>: what happens to an
/// object/array child node whose <see cref="ImportNode.JoinKey"/> doesn't resolve to an existing row.
/// Only meaningful for <see cref="ImportNodeKind.Object"/>/<see cref="ImportNodeKind.Array"/> nodes —
/// root rows are always match-only (see <see cref="UnmatchedRootPolicy"/>, which has no "insert"
/// option at all).</summary>
public static class OnMissingChildPolicy
{
    public const string Insert = "insert";
    public const string Reject = "reject";
}

/// <summary>Discriminator values for <c>ImportDefinitionEntity.UnmatchedRootPolicy</c>. Deliberately
/// excludes an "auto-create" option: every inbound record's correlation key must resolve to an
/// existing root row, or it's excluded from the accepted set per this policy (see
/// import-definitions.md §1).</summary>
public static class UnmatchedRootPolicy
{
    public const string Reject = "reject";
    public const string Quarantine = "quarantine";
}

/// <summary>
/// One node in a recursive import tree — the write-side mirror of <see cref="ExportNode"/>: same
/// shape (root/scalar-field/object/array, arbitrarily nested via <see cref="Children"/>, reusing
/// <see cref="FieldMapping"/> verbatim for value coercion), walked in the opposite direction.
/// Deliberately its own type rather than merged into <see cref="ExportNode"/>: <see cref="SourceKey"/>
/// reads from inbound JSON where <c>ExportNode.SourceField</c> reads from SQL,
/// <see cref="TargetColumn"/> writes to SQL where <c>ExportNode.TargetKey</c> writes to JSON, and
/// <see cref="OnMissingChild"/> is a write-only policy with no export-side analogue — forcing both
/// into one type would mean nullable fields that are meaningless half the time.
/// </summary>
public record ImportNode(
    string SourceKey,
    string Kind,
    string? TargetColumn,
    string? RelatedTable,
    string? JoinKey,
    string? SourceJoinKey,
    string OnMissingChild,
    FieldMapping? Mapping,
    ImportNode[] Children,
    bool Enabled
);

/// <summary>
/// Deserializes an <see cref="ImportNode"/> tree stored as JSON (on
/// <c>ImportDefinitionEntity.RootNode</c>), backfilling missing properties the same way
/// <see cref="ExportNodeJson"/> does: a tree saved before a property existed must not crash every
/// consumer that dereferences it. Every read of a persisted <see cref="ImportNode"/> tree must go
/// through this instead of a raw <see cref="JsonSerializer.Deserialize{TValue}(string, JsonSerializerOptions?)"/> call.
/// </summary>
public static class ImportNodeJson
{
    public static ImportNode? Deserialize(string json)
    {
        var node = JsonSerializer.Deserialize<ImportNode>(json);
        return node is null ? null : Normalize(node);
    }

    public static string Serialize(ImportNode node) => JsonSerializer.Serialize(node);

    // Recurses into Children so a tree missing Kind/OnMissingChild/Mapping properties at any depth
    // gets the same backfill, matching ExportNodeJson.Normalize's recursive pattern.
    private static ImportNode Normalize(ImportNode node) =>
        node with
        {
            Kind = string.IsNullOrWhiteSpace(node.Kind) ? ImportNodeKind.Object : node.Kind,
            OnMissingChild = string.IsNullOrWhiteSpace(node.OnMissingChild)
                ? OnMissingChildPolicy.Reject
                : node.OnMissingChild,
            Children = (node.Children ?? []).Select(Normalize).ToArray(),
            Mapping = node.Mapping is null ? null : ExportNodeJson.NormalizeMapping(node.Mapping),
        };
}
