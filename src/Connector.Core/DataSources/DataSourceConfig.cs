namespace Connector.Core.DataSources;

/// <summary>
/// Connection parameters for the configured ERP data source. Provider-agnostic by design: <see cref="Type"/>
/// says which <see cref="IDataSourceProvider"/> (resolved via <see cref="IDataSourceProviderResolver"/>)
/// interprets the rest of these fields — a relational source (<see cref="DataSourceType.PostgreSql"/>/
/// <see cref="DataSourceType.MariaDb"/>) uses <see cref="Host"/>/<see cref="Port"/>/<see cref="Database"/>,
/// an HTTP API source (<see cref="DataSourceType.ServiceNowTableApi"/>/
/// <see cref="DataSourceType.ServiceNowSqlApi"/>) uses <see cref="InstanceUrl"/> instead. See
/// knowledge/architecture/data-source-configuration.md for the full design and back-compat contract.
/// </summary>
/// <remarks>
/// Arbeitsauftrag 3: generalized from a positional record with required Host/Port/Database (Arbeitsauftrag
/// 2's <c>ErpConnectionConfig</c> shape) into this init-only shape so a non-relational source doesn't need
/// to fake a Host/Port/Database it doesn't have. <b>Back-compat is load-bearing, not incidental:</b> every
/// property here defaults exactly the way a JSON payload missing that property already deserialized under
/// the old positional record — <see cref="Type"/> to <see cref="DataSourceType.PostgreSql"/> (enum's 0
/// value), <see cref="Username"/>/<see cref="Password"/> to <c>""</c> (the old record had no way to omit
/// them, so no stored row ever needs this default in practice, but it keeps this record's own parameterless
/// construction total) — so every <c>AppSettings</c> row persisted before this change, and before
/// Arbeitsauftrag 2's own <c>Type</c> field existed, keeps deserializing as an equivalent PostgreSql config,
/// unchanged. No EF migration is involved: <c>AppSetting.Value</c> is a schemaless encrypted JSON blob
/// (<see cref="Connector.Infrastructure.EncryptedStringConverter"/>), not a typed column, so evolving this
/// record's shape has never required one.
/// </remarks>
public sealed record DataSourceConfig
{
    /// <summary>Defaults to <see cref="DataSourceType.PostgreSql"/> (the enum's 0 value) — see this record's
    /// own back-compat remarks.</summary>
    public DataSourceType Type { get; init; }

    /// <summary>Relational sources only (<see cref="DataSourceType.PostgreSql"/>/<see cref="DataSourceType.MariaDb"/>).</summary>
    public string? Host { get; init; }

    /// <summary>Relational sources only.</summary>
    public int? Port { get; init; }

    /// <summary>Relational sources only.</summary>
    public string? Database { get; init; }

    /// <summary>HTTP API sources only (<see cref="DataSourceType.ServiceNowTableApi"/>/
    /// <see cref="DataSourceType.ServiceNowSqlApi"/>) — the base URL of the ServiceNow instance, e.g.
    /// <c>https://acme.service-now.com</c>.</summary>
    public string? InstanceUrl { get; init; }

    public string Username { get; init; } = "";

    public string Password { get; init; } = "";

    /// <summary>
    /// Security-review finding SR-03: was previously hardcoded to Npgsql's "Prefer" everywhere (silently
    /// downgrades to an unencrypted connection if the server doesn't offer TLS) with no way for an operator to
    /// require and verify it instead. One of Npgsql's <c>SslMode</c> names — "Disable", "Allow", "Prefer",
    /// "Require", "VerifyCA", or "VerifyFull" (validated in <c>Connector.Api.Endpoints.ConnectionEndpoints</c>)
    /// — or null/empty to keep that same "Prefer" default. Relational sources only.
    /// </summary>
    public string? SslMode { get; init; }

    /// <summary>The only password-related fact an API response may ever expose (see
    /// <c>Connector.Api.Endpoints.ConnectionEndpoints</c>'s <c>GET /api/connection</c>) — never
    /// <see cref="Password"/> itself.</summary>
    public bool HasPassword => !string.IsNullOrEmpty(Password);

    /// <summary>
    /// Overridden so the compiler-generated, all-properties <c>ToString()</c> a bare record would otherwise
    /// get can never print <see cref="Password"/> in plaintext — e.g. into a log via <c>{Config}</c>/
    /// <c>$"{config}"</c>, or a debugger's default display. Redacts <see cref="Password"/> to a fixed marker
    /// and reports <see cref="HasPassword"/> instead, mirroring the API's own "hasPassword, never the value"
    /// contract.
    /// </summary>
    public override string ToString() =>
        $"DataSourceConfig {{ Type = {Type}, Host = {Host}, Port = {Port}, Database = {Database}, "
        + $"InstanceUrl = {InstanceUrl}, Username = {Username}, HasPassword = {HasPassword}, "
        + $"SslMode = {SslMode} }}";
}
