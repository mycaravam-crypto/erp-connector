using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Connector.Core.DataSources;
using Connector.Infrastructure;

namespace Connector.Integration.Tests;

/// <summary>
/// HTTP-layer coverage for <c>GET /api/connection</c>: the response must never carry a
/// password, in any field or under any name — the most it may say is <c>hasPassword</c>. Seeds
/// <c>SettingsKeys.ErpConnection</c> directly via <see cref="ApiSettings"/> rather than going through
/// <c>POST /api/connection</c>, so this doesn't need a live Postgres <c>testdb</c>.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ConnectionEndpointsHttpTests
{
    private readonly ApiFactory _factory;

    public ConnectionEndpointsHttpTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task GetConnection_StoredConnectionHasPassword_ResponseNeverContainsIt()
    {
        const string password = "extremely-secret-erp-password";
        await _factory.SetAsync(
            SettingsKeys.ErpConnection,
            new DataSourceConfig
            {
                Type = DataSourceType.PostgreSql,
                Host = "erp.example",
                Port = 5432,
                Database = "erp",
                Username = "reader",
                Password = password,
            }
        );

        using var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/connection");
        var rawBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(password, rawBody, StringComparison.Ordinal);
        // "hasPassword" is the one allowed exception — checks for the literal JSON key "password" (quoted),
        // which "hasPassword" (quoted as a whole) never matches.
        Assert.DoesNotContain("\"password\"", rawBody, StringComparison.OrdinalIgnoreCase);

        var body = JsonSerializer.Deserialize<JsonElement>(rawBody);
        Assert.True(body.GetProperty("hasPassword").GetBoolean());
        Assert.Equal("erp.example", body.GetProperty("host").GetString());
        Assert.Equal("reader", body.GetProperty("username").GetString());
    }

    [Fact]
    public async Task GetConnection_StoredConnectionWithoutPassword_HasPasswordIsFalse()
    {
        await _factory.SetAsync(
            SettingsKeys.ErpConnection,
            new DataSourceConfig
            {
                Type = DataSourceType.PostgreSql,
                Host = "erp2.example",
                Port = 5432,
                Database = "erp2",
                Username = "reader2",
            }
        );

        using var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/connection");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiAuth.Json);

        Assert.False(body.GetProperty("hasPassword").GetBoolean());
    }

    [Fact]
    public async Task PostConnection_UnknownDataSourceType_ReturnsBadRequest()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/connection",
            new
            {
                type = 999,
                username = "u",
                password = "p",
            },
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostConnection_MissingRequiredFieldsForType_ReturnsBadRequest()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();

        // PostgreSql without Host/Port/Database — an invalid combination for this type.
        var response = await client.PostAsJsonAsync(
            "/api/connection",
            new
            {
                type = 0,
                username = "u",
                password = "p",
            },
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // A modeled but unimplemented type (ServiceNow SQL API) gets a readable 400, not the resolver's internal
    // "No IDataSourceProvider is registered" wording.
    [Fact]
    public async Task PostConnection_UnimplementedDataSourceType_ReturnsReadableBadRequest()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/connection",
            new
            {
                type = 3,
                // An IP literal (TEST-NET-3), so the SSRF host check needs no DNS.
                instanceUrl = "https://203.0.113.10",
                username = "u",
                password = "p",
            },
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("not supported by this connector version", body);
        Assert.DoesNotContain("IDataSourceProvider", body);
    }

    [Theory]
    [InlineData("http://acme.service-now.com")]
    [InlineData("acme.service-now.com")]
    [InlineData("https://169.254.169.254")]
    public async Task PostConnection_ServiceNowInstanceUrlNotHttpsOrBlocked_ReturnsBadRequest(string instanceUrl)
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/connection",
            new
            {
                type = 2,
                instanceUrl,
                username = "u",
                password = "p",
            },
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
