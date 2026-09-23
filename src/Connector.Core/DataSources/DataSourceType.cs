namespace Connector.Core.DataSources;

/// <summary>
/// Identifies which backend an <see cref="IDataSourceProvider"/> implementation targets. Resolved to a
/// concrete provider via <see cref="IDataSourceProviderResolver"/>.
/// </summary>
/// <remarks>
/// <see cref="PostgreSql"/> is the enum's default (0) so a <see cref="DataSourceConfig"/> persisted as JSON
/// before this field existed — every <c>AppSettings</c> row stored to date, since PostgreSQL is the only
/// backend the connector has ever supported — deserializes as <see cref="PostgreSql"/> rather than some
/// arbitrary member.
/// </remarks>
public enum DataSourceType
{
    PostgreSql = 0,

    /// <summary>MariaDB (Arbeitsauftrag 7), over MySqlConnector. Uses the relational fields
    /// (<c>Host</c>/<c>Port</c>/<c>Database</c>/<c>SslMode</c>) like <see cref="PostgreSql"/>.</summary>
    MariaDb = 1,

    /// <summary>ServiceNow's Table API (REST, one JSON object per record). Arbeitsauftrag 3: modeled so a
    /// <see cref="DataSourceConfig"/> can describe it (<c>InstanceUrl</c>/<c>Username</c>/<c>Password</c>),
    /// but not implemented yet — <see cref="IDataSourceProviderResolver.Resolve"/> throws
    /// <see cref="UnsupportedDataSourceException"/> for this value.</summary>
    ServiceNowTableApi = 2,

    /// <summary>ServiceNow's (deprecated but still deployed) SOAP/SQL-style query API. Arbeitsauftrag 3: same
    /// modeling-only status as <see cref="ServiceNowTableApi"/> — not implemented yet.</summary>
    ServiceNowSqlApi = 3,
}
