using Connector.Core.DataSources;

namespace Connector.Infrastructure.DataSources;

/// <summary>
/// Whether a <see cref="DataSourceConfig"/> can only ever talk to its source encrypted (Arbeitsauftrag 11). A
/// relational source is when its <c>SslMode</c> makes TLS mandatory (<c>Require</c>, <c>VerifyCA</c>,
/// <c>VerifyFull</c> — one vocabulary for PostgreSQL and MariaDB); unset/<c>Prefer</c>/<c>Allow</c> silently fall
/// back to plaintext and <c>Disable</c> never encrypts. ServiceNow is always HTTPS (enforced by its client). In
/// production, <c>POST /api/connection</c> refuses anything else unless
/// <c>DataSources:AllowUnencryptedConnections</c> is explicitly <c>true</c>.
/// </summary>
public static class TransportSecurity
{
    public const string AllowUnencryptedSetting = "DataSources:AllowUnencryptedConnections";

    private static readonly HashSet<string> MandatoryTlsModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Require",
        "VerifyCA",
        "VerifyFull",
    };

    public static bool IsAlwaysEncrypted(DataSourceConfig config) =>
        config.Type is not (DataSourceType.PostgreSql or DataSourceType.MariaDb)
        || (config.SslMode is not null && MandatoryTlsModes.Contains(config.SslMode.Trim()));

    public static string UnencryptedRefusal(DataSourceConfig config) =>
        $"TLS mode '{(string.IsNullOrWhiteSpace(config.SslMode) ? "Prefer (default)" : config.SslMode)}' can fall back to "
        + "an unencrypted connection, which is not allowed in production. Choose Require, VerifyCA or VerifyFull, "
        + $"or explicitly allow unencrypted connections with {AllowUnencryptedSetting}=true.";
}
