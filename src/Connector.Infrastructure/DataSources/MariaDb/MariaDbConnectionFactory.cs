using Connector.Core.DataSources;
using MySqlConnector;

namespace Connector.Infrastructure.DataSources.MariaDb;

/// <summary>
/// Builds and opens <see cref="MySqlConnection"/>s for a <see cref="DataSourceType.MariaDb"/>
/// <see cref="DataSourceConfig"/>. Every field is set as a typed builder property, never interpolated into the
/// connection-string text, so no value (e.g. a password containing <c>;Server=evil</c>) can add or override a
/// key — the same SR-02 rule as <c>PostgreSqlDataSourceProvider.BuildConnectionString</c>.
/// </summary>
public static class MariaDbConnectionFactory
{
    public const int DefaultPort = 3306;

    public static string BuildConnectionString(DataSourceConfig config) =>
        new MySqlConnectionStringBuilder
        {
            Server = config.Host,
            Port = (uint)(config.Port ?? DefaultPort),
            Database = config.Database,
            UserID = config.Username,
            Password = config.Password,
            SslMode = ParseSslMode(config.SslMode),
            ConnectionTimeout = 5,
            DefaultCommandTimeout = 10,
            // A '0000-00-00' date (legal in MariaDB unless NO_ZERO_DATE) reads as DateTime.MinValue instead of
            // failing the whole row.
            ConvertZeroDateTime = true,
        }.ConnectionString;

    public static async Task<MySqlConnection> OpenAsync(DataSourceConfig config, CancellationToken cancellationToken)
    {
        var connection = new MySqlConnection(BuildConnectionString(config));
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    // DataSourceConfig.SslMode uses one vocabulary for every relational source — the Npgsql names that
    // RelationalConnectionRules validates at save time — mapped here onto MySqlConnector's modes.
    // Unset/unknown falls back to Preferred (TLS when the server offers it), mirroring Postgres's Prefer default.
    private static MySqlSslMode ParseSslMode(string? sslMode) =>
        sslMode?.Trim().ToUpperInvariant() switch
        {
            "DISABLE" => MySqlSslMode.None,
            "REQUIRE" => MySqlSslMode.Required,
            "VERIFYCA" => MySqlSslMode.VerifyCA,
            "VERIFYFULL" => MySqlSslMode.VerifyFull,
            _ => MySqlSslMode.Preferred,
        };
}
