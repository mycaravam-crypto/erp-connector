using System.Net;
using System.Text.Json;

namespace Connector.Integration.Tests;

/// <summary>
/// First real HTTP-layer coverage for <c>Connector.Api</c> (previously 9.4% line coverage, with every
/// endpoint file at a flat 0%) — boots the actual <see cref="Program"/> pipeline via <see cref="ApiFactory"/>
/// instead of calling handler methods directly. <c>/api/health</c> needs no auth, so it's the simplest
/// place to prove the harness itself (migrations, DI, middleware) boots correctly end to end.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class HealthEndpointHttpTests
{
    private readonly ApiFactory _factory;

    public HealthEndpointHttpTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task GetHealth_StagingWritableAndDbReachable_ReturnsHealthy()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("healthy", doc.RootElement.GetProperty("status").GetString());
        Assert.True(doc.RootElement.GetProperty("checks").GetProperty("log_db").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("checks").GetProperty("staging").GetBoolean());
    }

    [Fact]
    public async Task GetHealth_ResponseCarriesSecurityHeadersFromProgramMiddleware()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }
}
