namespace Connector.Core.DataSources;

/// <summary>
/// Provider-native SQL text (plus optional named parameters) for
/// <see cref="IDataSourceProvider.ExecuteNativeAsync"/>. The database-neutral counterpart is
/// <see cref="SourceQuery"/>.
/// </summary>
/// <remarks>
/// Two producers exist: a provider's own compiler for <see cref="SourceQuery"/> (e.g.
/// <c>Connector.Infrastructure.DataSources.PostgreSql.PostgreSqlQueryCompiler</c>), and
/// <c>DynamicExportService</c>'s export builders (JSON object/array aggregation trees, rendered through the
/// provider's SQL dialect), which the neutral model cannot express yet — see knowledge/architecture/source-query-model.md §5. The provider only executes this
/// text and materializes rows generically; it never parses it. Nothing in <c>Connector.Core</c> builds one.
/// <para><see cref="ReturnNativeText"/>: when true, every value comes back as the backend's own text rendering
/// of it (e.g. PostgreSQL's <c>2024-03-15 10:11:12.5</c>, <c>t</c>, <c>{1,2}</c>) instead of the provider's
/// default stringification, and <see cref="QueryResultColumn.DataType"/> names the backend type to interpret it
/// by. The export tree engine uses this to render typed JSON values in C# exactly as the database would have.</para>
/// </remarks>
public sealed record NativeSqlQuery(
    string Sql,
    IReadOnlyDictionary<string, object?>? Parameters = null,
    int? CommandTimeoutSeconds = null,
    bool ReturnNativeText = false
);
