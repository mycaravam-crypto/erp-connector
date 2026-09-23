namespace Connector.Core.DataSources;

/// <summary>
/// Provider-native SQL text (plus optional named parameters) for
/// <see cref="IDataSourceProvider.ExecuteNativeAsync"/>. Named <c>SourceQuery</c> before Arbeitsauftrag 4,
/// which gave that name to the database-neutral <see cref="SourceQuery"/> model instead.
/// </summary>
/// <remarks>
/// Two producers exist: a provider's own compiler for <see cref="SourceQuery"/> (e.g.
/// <c>Connector.Infrastructure.DataSources.PostgreSql.PostgreSqlQueryCompiler</c>), and
/// <c>DynamicExportService</c>'s export builders (JSON object/array aggregation trees, rendered through the
/// provider's SQL dialect), which the neutral model cannot express yet — see knowledge/architecture/source-query-model.md §5. The provider only executes this
/// text and materializes rows generically; it never parses it. Nothing in <c>Connector.Core</c> builds one.
/// </remarks>
public sealed record NativeSqlQuery(
    string Sql,
    IReadOnlyDictionary<string, object?>? Parameters = null,
    int? CommandTimeoutSeconds = null
);
