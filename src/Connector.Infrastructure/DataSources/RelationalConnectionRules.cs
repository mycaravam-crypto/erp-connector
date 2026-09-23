using Connector.Core.DataSources;

namespace Connector.Infrastructure.DataSources;

/// <summary>
/// The connection rules every relational (Host/Port/Database) provider shares, so PostgreSQL and MariaDB validate
/// and judge a <see cref="DataSourceConfig"/> identically: the required fields, and one <c>SslMode</c> vocabulary
/// (the Npgsql names — each provider maps them onto its own driver's modes).
/// </summary>
public static class RelationalConnectionRules
{
    /// <summary>Every accepted <c>SslMode</c> (case-insensitive); unset means the provider's default (Prefer).</summary>
    public static readonly IReadOnlySet<string> SslModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Disable",
        "Allow",
        "Prefer",
        "Require",
        "VerifyCA",
        "VerifyFull",
    };

    private static readonly HashSet<string> MandatoryTlsModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Require",
        "VerifyCA",
        "VerifyFull",
    };

    public static string? Validate(DataSourceConfig config)
    {
        if (
            string.IsNullOrWhiteSpace(config.Host)
            || config.Port is null
            || string.IsNullOrWhiteSpace(config.Database)
            || string.IsNullOrWhiteSpace(config.Username)
        )
            return "Host, Port, Database, and Username are required for this data source type.";
        if (!string.IsNullOrWhiteSpace(config.SslMode) && !SslModes.Contains(config.SslMode.Trim()))
            return "SslMode must be one of: Disable, Allow, Prefer, Require, VerifyCA, VerifyFull.";
        return null;
    }

    /// <summary>Only <c>Require</c>/<c>VerifyCA</c>/<c>VerifyFull</c> make TLS mandatory; unset/<c>Prefer</c>/
    /// <c>Allow</c> silently fall back to plaintext and <c>Disable</c> never encrypts.</summary>
    public static bool IsAlwaysEncrypted(DataSourceConfig config) =>
        config.SslMode is not null && MandatoryTlsModes.Contains(config.SslMode.Trim());
}
