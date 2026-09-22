namespace Connector.Core.DataSources;

/// <summary>
/// A single query for <see cref="IDataSourceProvider.ExecuteAsync"/> to run: provider-native SQL text (built
/// by the caller — e.g. <c>Connector.Infrastructure.DynamicExportService</c>'s existing Postgres-dialect query
/// builders) plus optional named parameters. The provider only executes this text against its backend and
/// materializes rows generically — it does not parse or understand the query itself, so the SQL dialect
/// (identifier quoting, JSON aggregation, casts, …) stays exactly what it was pre-abstraction.
/// </summary>
public record SourceQuery(
    string Sql,
    IReadOnlyDictionary<string, object?>? Parameters = null,
    int? CommandTimeoutSeconds = null
);

/// <summary>
/// Generic, provider-agnostic row set returned by <see cref="IDataSourceProvider.ExecuteAsync"/>. Every value
/// is already stringified by the provider (e.g. a Postgres date/timestamp column is coerced to ISO-8601 the
/// same way the pre-abstraction code did) so callers never need provider-specific type information. A null
/// dictionary value means the source column was NULL.
/// </summary>
public record QueryResult(IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows);
