namespace Connector.Core.DataSources;

/// <summary>
/// Connection parameters for the configured ERP data source. Provider-agnostic by design: <see cref="Type"/>
/// says which <see cref="IDataSourceProvider"/> (resolved via <see cref="IDataSourceProviderResolver"/>)
/// interprets the rest of these fields. Today <c>Connector.Infrastructure.PostgreSqlDataSourceProvider</c> is
/// the only implementation — Host/Port/Database/Username/Password/SslMode map onto its Npgsql connection
/// string. Renamed from <c>ErpConnectionConfig</c> (its pre-Arbeitsauftrag-2 name) to match
/// <see cref="IDataSourceProvider"/>'s signatures — see knowledge/architecture/data-source-abstraction.md.
/// </summary>
/// <param name="Type">
/// Defaults to <see cref="DataSourceType.PostgreSql"/> (the enum's 0 value) so every <c>AppSettings</c> row
/// persisted before this field existed, and every existing test construction that predates it, keeps
/// deserializing/compiling as a Postgres connection, unchanged.
/// </param>
/// <param name="SslMode">
/// Security-review finding SR-03: was previously hardcoded to Npgsql's "Prefer" everywhere (silently
/// downgrades to an unencrypted connection if the server doesn't offer TLS) with no way for an operator to
/// require and verify it instead. One of Npgsql's <c>SslMode</c> names — "Disable", "Allow", "Prefer",
/// "Require", "VerifyCA", or "VerifyFull" (validated in <c>Connector.Api.Endpoints.ConnectionEndpoints</c>)
/// — or null/empty to keep that same "Prefer" default. Optional, so every existing 5-argument construction of
/// this record (tests, stored settings predating this field) keeps compiling and behaving exactly as before.
/// Only meaningful when <see cref="Type"/> is <see cref="DataSourceType.PostgreSql"/>.
/// </param>
public record DataSourceConfig(
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    string? SslMode = null,
    DataSourceType Type = DataSourceType.PostgreSql
);
