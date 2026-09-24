namespace Connector.Core.DataSources;

/// <summary>One column of a <see cref="SourceTable"/>, as reported by <see cref="IDataSourceProvider.ReadSchemaAsync"/>.</summary>
/// <remarks>
/// <see cref="IsIdentity"/>/<see cref="IsGenerated"/>: for <c>PostgreSqlDataSourceProvider</c>, populated from
/// <c>information_schema.columns</c>' own <c>is_identity</c>/<c>is_generated</c> columns. The save-time
/// AllowedWritableColumns validator uses them to reject a TargetColumn the ERP itself manages (an identity
/// sequence or a GENERATED ... STORED expression). Both default false.
/// </remarks>
public record SourceColumn(
    string Name,
    string Type,
    bool Nullable,
    bool PrimaryKey,
    string? ForeignKeyTable = null,
    string? ForeignKeyColumn = null,
    bool IsIdentity = false,
    bool IsGenerated = false
);

/// <summary>One table of a <see cref="SourceSchema"/>.</summary>
public record SourceTable(string Name, string Description, SourceColumn[] Columns);

/// <summary>The live (or demo-fallback) schema of the configured data source, as returned by
/// <see cref="IDataSourceProvider.ReadSchemaAsync"/>/<see cref="IDataSourceProvider.TestConnectionAsync"/>.</summary>
public record SourceSchema(string ConnectionLabel, SourceTable[] Tables);
