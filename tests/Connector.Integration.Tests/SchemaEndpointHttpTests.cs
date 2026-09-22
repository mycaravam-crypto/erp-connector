using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Connector.Api;
using Connector.Core.Schema;

namespace Connector.Integration.Tests;

/// <summary>HTTP-layer coverage for <see cref="SchemaEndpoints"/> against the real <see cref="Program"/>
/// pipeline (see <see cref="ApiFactory"/>/<see cref="ApiCollection"/>) — a static, ERP-independent
/// document, so this only needs to confirm the auth guard and the fixed response shape.</summary>
[Collection(ApiCollection.Name)]
public sealed class SchemaEndpointHttpTests
{
    private readonly ApiFactory _factory;

    public SchemaEndpointHttpTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task GetSchema_WithoutAuth_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/schema");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSchema_Authenticated_ReturnsFixedIcdColumnsWithCurrentVersion()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/schema");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiAuth.Json);
        Assert.Equal(ExportSchema.Version, body.GetProperty("version").GetString());
        var columns = body.GetProperty("columns").EnumerateArray().ToList();
        Assert.NotEmpty(columns);
        Assert.Contains(columns, c => c.GetProperty("name").GetString() == "guid");
    }
}
