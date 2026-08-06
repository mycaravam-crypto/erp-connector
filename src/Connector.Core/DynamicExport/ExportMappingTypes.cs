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

/// <summary>The complete mapping config for one source table, including field remaps and relation flattening.</summary>
public record ExportMappingConfig(string SourceTable, ExportMappingField[] Fields, ExportMappingRelation[] Relations);

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

    private static ExportMappingConfig Normalize(ExportMappingConfig config) =>
        config with
        {
            Relations = (config.Relations ?? []).Select(NormalizeRelation).ToArray(),
        };

    private static ExportMappingRelation NormalizeRelation(ExportMappingRelation r) =>
        r with
        {
            Fields = r.Fields ?? [],
            FlattenStrategy = r.FlattenStrategy ?? DefaultFlattenStrategy,
            Delimiter = r.Delimiter ?? DefaultDelimiter,
        };
}
