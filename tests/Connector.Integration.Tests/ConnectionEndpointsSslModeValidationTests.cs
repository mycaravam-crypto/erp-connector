using Connector.Api.Endpoints;

namespace Connector.Integration.Tests;

/// <summary>
/// Coverage for <see cref="ConnectionEndpoints.IsValidSslMode"/> (security-review finding SR-03: the ERP
/// connection's SSL mode was hardcoded to Npgsql's "Prefer" — silently unencrypted whenever the server
/// doesn't offer TLS — with no way for an operator to require and verify it instead; this validates the
/// new opt-in <see cref="Connector.Core.DynamicExport.ErpConnectionConfig.SslMode"/> field at save time).
/// </summary>
public sealed class ConnectionEndpointsSslModeValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Disable")]
    [InlineData("Allow")]
    [InlineData("Prefer")]
    [InlineData("Require")]
    [InlineData("VerifyCA")]
    [InlineData("VerifyFull")]
    [InlineData("verifyfull")] // case-insensitive, matching Enum.TryParse(ignoreCase: true)
    public void ValidOrUnset_Accepted(string? sslMode)
    {
        Assert.True(ConnectionEndpoints.IsValidSslMode(sslMode));
    }

    [Theory]
    [InlineData("Trust")]
    [InlineData("verify-full")]
    [InlineData("VerifyFull; DROP TABLE users")]
    public void Unrecognized_Rejected(string sslMode)
    {
        Assert.False(ConnectionEndpoints.IsValidSslMode(sslMode));
    }
}
