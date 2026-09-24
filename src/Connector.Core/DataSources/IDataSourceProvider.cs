namespace Connector.Core.DataSources;

/// <summary>
/// Abstraction over one kind of ERP data source backend — see
/// knowledge/architecture/data-source-abstraction.md. Implemented for PostgreSQL, MariaDB and the ServiceNow Table
/// API (<c>Connector.Infrastructure.DataSources.*</c>); obtained via <see cref="IDataSourceProviderResolver"/>,
/// never constructed directly outside DI registration. Where providers differ, <see cref="Capabilities"/> says so.
/// </summary>
public interface IDataSourceProvider
{
    /// <summary>The <see cref="DataSourceType"/> this provider implements — what
    /// <see cref="IDataSourceProviderResolver.Resolve"/> matches against.</summary>
    DataSourceType Type { get; }

    /// <summary>What this provider supports beyond the common contract — see <see cref="DataSourceCapabilities"/>.</summary>
    DataSourceCapabilities Capabilities { get; }

    /// <summary>Whether <paramref name="config"/> has what this provider needs to connect (required fields, a valid
    /// TLS mode or instance URL): a user-facing message, or null when it's usable. Checked before any connection
    /// attempt; the only place per-source-type config rules live.</summary>
    string? ValidateConfig(DataSourceConfig config);

    /// <summary>The host a connection with the (validated) <paramref name="config"/> reaches — what the SSRF check
    /// in <c>POST /api/connection</c> resolves.</summary>
    string TargetHost(DataSourceConfig config);

    /// <summary>True when <paramref name="config"/> can only ever reach the source encrypted — never falling back
    /// to plaintext.</summary>
    bool IsAlwaysEncrypted(DataSourceConfig config);

    /// <summary>Opens a connection against <paramref name="config"/> and reads back its schema, without
    /// persisting anything. Never throws for a reachability/credential failure — that's reported via
    /// <see cref="TestConnectionResult.Success"/>/<see cref="TestConnectionResult.Error"/> instead, sanitized
    /// of any connection-string/credential detail.</summary>
    Task<TestConnectionResult> TestConnectionAsync(DataSourceConfig config, CancellationToken cancellationToken);

    /// <summary>Reads the live schema (tables/columns/PK/FK) of the data source described by
    /// <paramref name="config"/>. Throws on failure — unlike <see cref="TestConnectionAsync"/>, this is called
    /// once a connection is already known-good and a failure here is a real, reportable error.</summary>
    Task<SourceSchema> ReadSchemaAsync(DataSourceConfig config, CancellationToken cancellationToken);

    /// <summary>Validates <paramref name="query"/> against the data source's live schema
    /// (<see cref="SourceQueryValidator"/>), compiles it into the provider's own dialect with every filter value
    /// bound as a parameter, executes it, and returns its rows generically. Throws
    /// <see cref="InvalidSourceQueryException"/> — before touching any data — for an unknown table/column or a
    /// malformed query; an execution failure the caller needs to inspect surfaces as
    /// <see cref="DataSourceQueryException"/>.</summary>
    Task<QueryResult> ExecuteAsync(DataSourceConfig config, SourceQuery query, CancellationToken cancellationToken);

    /// <summary>Executes caller-built, provider-native SQL as-is and returns its rows generically — the path
    /// <c>DynamicExportService</c>'s JSON-tree builders still use for what <see cref="SourceQuery"/> cannot
    /// express yet (knowledge/architecture/source-query-model.md §5). Same error contract as
    /// <see cref="ExecuteAsync"/>, minus schema validation.</summary>
    Task<QueryResult> ExecuteNativeAsync(
        DataSourceConfig config,
        NativeSqlQuery query,
        CancellationToken cancellationToken
    );
}
