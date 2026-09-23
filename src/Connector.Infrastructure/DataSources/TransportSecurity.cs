using Connector.Core.DataSources;

namespace Connector.Infrastructure.DataSources;

/// <summary>
/// The production transport rule (Arbeitsauftrag 11): <c>POST /api/connection</c> refuses a config its provider
/// doesn't report as always encrypted (<see cref="Connector.Core.DataSources.IDataSourceProvider.IsAlwaysEncrypted"/>)
/// unless <c>DataSources:AllowUnencryptedConnections</c> is explicitly <c>true</c>.
/// </summary>
public static class TransportSecurity
{
    public const string AllowUnencryptedSetting = "DataSources:AllowUnencryptedConnections";

    public static string UnencryptedRefusal(DataSourceConfig config) =>
        $"TLS mode '{(string.IsNullOrWhiteSpace(config.SslMode) ? "Prefer (default)" : config.SslMode)}' can fall back to "
        + "an unencrypted connection, which is not allowed in production. Choose Require, VerifyCA or VerifyFull, "
        + $"or explicitly allow unencrypted connections with {AllowUnencryptedSetting}=true.";
}
