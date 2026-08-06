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
