using System.Text.Json;

namespace Connector.Core.DynamicExport;

/// <summary>A single column mapping with source name, target export name, and enabled flag.</summary>
public record ExportMappingField(string SourceName, string TargetName, bool Enabled);

/// <summary>A single value column pulled from a related table, with its own export name and enabled flag.</summary>
public record ExportMappingRelationField(string SourceField, string TargetField, bool Enabled);

/// <summary>A 1:N relation config for joining and flattening one or more columns of a related table into the parent row.</summary>
public record ExportMappingRelation(
    string RelatedTable,
    string JoinKey,
    string SourceJoinKey,
    bool Enabled,
    string FlattenStrategy,
    string Delimiter,
    ExportMappingRelationField[] Fields
);

/// <summary>A single scalar field pulled from a nested group's related table, exported as an object key.</summary>
public record ExportMappingNestedField(string SourceField, string TargetKey, bool Enabled);

/// <summary>
/// A JSON-only nested structure: either a single embedded object (<c>Kind="object"</c>, an N:1 lookup join)
/// or an array of embedded objects (<c>Kind="array"</c>, a 1:N join). <see cref="Children"/> lets groups nest
/// arbitrarily deep (e.g. item → manufacturer → addresses), each recursing within its parent's JSON expression.
/// </summary>
public record ExportMappingNestedGroup(
    string TargetKey,
    string RelatedTable,
    string JoinKey,
    string SourceJoinKey,
    bool Enabled,
    string Kind,
    ExportMappingNestedField[] Fields,
    ExportMappingNestedGroup[] Children
);

/// <summary>One key/value pair in the JSON export's metadata block. When <see cref="IsDynamicTimestamp"/> is
/// true, <see cref="Value"/> is ignored and the actual export timestamp is substituted at build time.</summary>
public record ExportJsonMetadataField(string Key, string Value, bool IsDynamicTimestamp);

/// <summary>Configures the JSON export envelope: an optional root key, the items array key, and metadata
/// field naming. Null on <see cref="ExportMappingConfig.JsonWrapper"/> preserves the legacy flat envelope.</summary>
public record ExportJsonWrapperConfig(
    string RootKey,
    string ItemsKey,
    string MetadataKey,
    ExportJsonMetadataField[] MetadataFields
);

/// <summary>The complete mapping config for one source table, including field remaps and relation flattening.
/// <see cref="NestedGroups"/> and <see cref="JsonWrapper"/> are JSON-export-only and additive: CSV/Excel export
/// and every pre-existing saved mapping ignore them entirely.</summary>
public record ExportMappingConfig(
    string SourceTable,
    ExportMappingField[] Fields,
    ExportMappingRelation[] Relations,
    ExportMappingNestedGroup[]? NestedGroups = null,
    ExportJsonWrapperConfig? JsonWrapper = null
);

/// <summary>ERP PostgreSQL connection parameters used to open a live Npgsql connection.</summary>
public record ErpConnectionConfig(string Host, int Port, string Database, string Username, string Password);

/// <summary>
/// Deserializes <see cref="ExportMappingConfig"/> stored as <c>AppSetting</c> JSON, repairing configs saved
/// before relations gained a <c>Fields</c> list, <c>Delimiter</c>, and relation-level <c>FlattenStrategy</c>:
/// System.Text.Json leaves a missing property at its type default — <see langword="null"/> for the string and
/// array properties here — rather than the value the old shape implied, which crashes every consumer that
/// dereferences it. Every read of the <c>export_mapping</c>/<c>export_presets</c> AppSetting values must go
/// through this instead of a raw <see cref="JsonSerializer.Deserialize{TValue}(string, JsonSerializerOptions?)"/>
/// call.
/// </summary>
public static class ExportMappingJson
{
    private const string DefaultFlattenStrategy = "string_join";
    private const string DefaultDelimiter = ", ";

    public static ExportMappingConfig? DeserializeConfig(string json)
    {
        var config = JsonSerializer.Deserialize<ExportMappingConfig>(json);
        return config is null ? null : Normalize(config);
    }

    public static Dictionary<string, ExportMappingConfig> DeserializePresets(string json)
    {
        var presets = JsonSerializer.Deserialize<Dictionary<string, ExportMappingConfig>>(json);
        if (presets is null)
            return new Dictionary<string, ExportMappingConfig>();
        return presets.ToDictionary(kv => kv.Key, kv => Normalize(kv.Value));
    }

    private const string DefaultNestedGroupKind = "object";

    private static ExportMappingConfig Normalize(ExportMappingConfig config) =>
        config with
        {
            Relations = (config.Relations ?? []).Select(NormalizeRelation).ToArray(),
            NestedGroups = (config.NestedGroups ?? []).Select(NormalizeNestedGroup).ToArray(),
            JsonWrapper = config.JsonWrapper is null ? null : NormalizeWrapper(config.JsonWrapper),
        };

    private static ExportMappingRelation NormalizeRelation(ExportMappingRelation r) =>
        r with
        {
            Fields = r.Fields ?? [],
            FlattenStrategy = r.FlattenStrategy ?? DefaultFlattenStrategy,
            Delimiter = r.Delimiter ?? DefaultDelimiter,
        };

    // Recurses into Children so nested groups at any depth get the same missing-property backfill
    // that protects the flat Relations shape above.
    private static ExportMappingNestedGroup NormalizeNestedGroup(ExportMappingNestedGroup g) =>
        g with
        {
            Fields = g.Fields ?? [],
            Children = (g.Children ?? []).Select(NormalizeNestedGroup).ToArray(),
            Kind = string.IsNullOrWhiteSpace(g.Kind) ? DefaultNestedGroupKind : g.Kind,
        };

    private static ExportJsonWrapperConfig NormalizeWrapper(ExportJsonWrapperConfig w) =>
        w with
        {
            MetadataFields = w.MetadataFields ?? [],
        };
}
