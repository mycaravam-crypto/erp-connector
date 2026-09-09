using Connector.Api.Endpoints;

namespace Connector.Integration.Tests;

/// <summary>
/// Coverage for <see cref="ConnectionEndpoints.ValidateHostAsync"/> (security audit finding: the ERP
/// connection endpoints let any authenticated caller point the server at an arbitrary host, which is
/// SSRF-able against cloud-provider instance-metadata services). Uses IP literals so no DNS resolution is
/// needed — deterministic and network-independent.
/// </summary>
public sealed class ConnectionEndpointsHostValidationTests
{
    [Theory]
    [InlineData("169.254.169.254")] // AWS/GCP/Azure/DigitalOcean instance metadata
    [InlineData("169.254.0.1")]
    [InlineData("fe80::1")]
    public async Task BlockedMetadataAddress_Rejected(string host)
    {
        var error = await ConnectionEndpoints.ValidateHostAsync(host, CancellationToken.None);

        Assert.NotNull(error);
        Assert.Contains("blocked", error);
    }

    [Theory]
    [InlineData("10.0.0.5")] // private ERP network — must remain usable
    [InlineData("192.168.1.10")]
    [InlineData("172.16.5.5")]
    [InlineData("127.0.0.1")]
    public async Task OrdinaryPrivateAddress_Allowed(string host)
    {
        var error = await ConnectionEndpoints.ValidateHostAsync(host, CancellationToken.None);

        Assert.Null(error);
    }
}
