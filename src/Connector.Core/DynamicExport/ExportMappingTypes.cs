namespace Connector.Core.DynamicExport;

public record ExportMappingField(string SourceName, string TargetName, bool Enabled);

public record ExportMappingStrategyOptions(string SourceField, string Delimiter);

public record ExportMappingRelation(
    string RelatedTable,
    string JoinKey,
    string SourceJoinKey,
    string TargetField,
    bool Enabled,
    string FlattenStrategy,
    ExportMappingStrategyOptions StrategyOptions
);

public record ExportMappingConfig(
    string SourceTable,
    ExportMappingField[] Fields,
    ExportMappingRelation[] Relations
);

public record ErpConnectionConfig(string Host, int Port, string Database, string Username, string Password);
