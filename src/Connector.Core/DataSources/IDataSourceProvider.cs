namespace Connector.Core.DataSources;

/// <summary>
/// Abstraction over one kind of ERP data source backend — see Arbeitsauftrag 2 /
/// knowledge/architecture/data-source-abstraction.md. Exactly one implementation exists today,
/// <c>Connector.Infrastructure.PostgreSqlDataSourceProvider</c>; obtained via
/// <see cref="IDataSourceProviderResolver"/>, never constructed directly outside DI registration.
/// </summary>
public interface IDataSourceProvider
{
    /// <summary>The <see cref="DataSourceType"/> this provider implements — what
    /// <see cref="IDataSourceProviderResolver.Resolve"/> matches against.</summary>
    DataSourceType Type { get; }

    /// <summary>Opens a connection against <paramref name="config"/> and reads back its schema, without
    /// persisting anything. Never throws for a reachability/credential failure — that's reported via
    /// <see cref="TestConnectionResult.Success"/>/<see cref="TestConnectionResult.Error"/> instead, sanitized
    /// of any connection-string/credential detail.</summary>
    Task<TestConnectionResult> TestConnectionAsync(DataSourceConfig config, CancellationToken cancellationToken);

    /// <summary>Reads the live schema (tables/columns/PK/FK) of the data source described by
    /// <paramref name="config"/>. Throws on failure — unlike <see cref="TestConnectionAsync"/>, this is called
    /// once a connection is already known-good and a failure here is a real, reportable error.</summary>
    Task<SourceSchema> ReadSchemaAsync(DataSourceConfig config, CancellationToken cancellationToken);

    /// <summary>Executes <paramref name="query"/>'s provider-native SQL against the data source described by
    /// <paramref name="config"/> and returns its rows generically. A query-execution failure the caller needs
    /// to inspect (not just log) surfaces as <see cref="DataSourceQueryException"/>.</summary>
    Task<QueryResult> ExecuteAsync(DataSourceConfig config, SourceQuery query, CancellationToken cancellationToken);
}
