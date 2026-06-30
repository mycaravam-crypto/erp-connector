namespace Connector.Core.DynamicExport;

/// <summary>A single column mapping with source name, target export name, and enabled flag.</summary>
public record ExportMappingField(string SourceName, string TargetName, bool Enabled);

/// <summary>Configuration for a flattening strategy applied to a 1:N relation (source field + delimiter).</summary>
public record ExportMappingStrategyOptions(string SourceField, string Delimiter);

/// <summary>A 1:N relation config for joining and flattening a related table into the parent row.</summary>
public record ExportMappingRelation(
    string RelatedTable,
    string JoinKey,
    string SourceJoinKey,
    string TargetField,
    bool Enabled,
    string FlattenStrategy,
    ExportMappingStrategyOptions StrategyOptions
);

/// <summary>The complete mapping config for one source table, including field remaps and relation flattening.</summary>
public record ExportMappingConfig(
    string SourceTable,
    ExportMappingField[] Fields,
    ExportMappingRelation[] Relations
);

/// <summary>ERP PostgreSQL connection parameters used to open a live Npgsql connection.</summary>
public record ErpConnectionConfig(string Host, int Port, string Database, string Username, string Password);
