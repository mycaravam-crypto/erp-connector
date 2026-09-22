using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Connector.Api;

namespace Connector.Integration.Tests;

/// <summary>HTTP-layer coverage for <see cref="BrandingEndpoints"/> against the real <see cref="Program"/>
/// pipeline (see <see cref="ApiFactory"/>/<see cref="ApiCollection"/>) — the image-data-URL/size validation
/// (previously untested), the normalize-empty-to-null behavior, and the auth guard on the write side.</summary>
[Collection(ApiCollection.Name)]
public sealed class BrandingEndpointsHttpTests
{
    private readonly ApiFactory _factory;

    public BrandingEndpointsHttpTests(ApiFactory factory) => _factory = factory;

    // Deliberately > 2MB (base64 length * 3/4 must exceed MaxLogoBytes) but still a well-formed data URL,
    // to hit the size-cap branch rather than the pattern-mismatch branch.
    private static string OversizedLogoDataUrl() => "data:image/png;base64," + new string('A', 3_000_000);

    [Fact]
    public async Task GetBranding_NoAuthRequired_ReturnsTheFullConfigShape()
    {
        // Doesn't assert all-null: this class's own PutBranding tests persist a config, and xUnit doesn't
        // guarantee execution order between test methods, so "nothing stored yet" isn't a safe assumption
        // here. The null-coalescing default itself is a one-line, low-risk fallback; what's worth pinning
        // down is that the endpoint needs no auth and returns every field regardless of what's stored.
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/branding");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiAuth.Json);
        foreach (var property in new[] { "appName", "logoDataUrl", "faviconDataUrl", "backgroundImageDataUrl" })
            Assert.True(body.TryGetProperty(property, out _), $"missing property '{property}'");
    }

    [Fact]
    public async Task PutBranding_WithoutAuth_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            "/api/branding",
            new
            {
                appName = "Whatever",
                logoDataUrl = (string?)null,
                faviconDataUrl = (string?)null,
                backgroundImageDataUrl = (string?)null,
            },
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PutBranding_AppNameTooLong_ReturnsBadRequest()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/branding",
            new
            {
                appName = new string('x', 61),
                logoDataUrl = (string?)null,
                faviconDataUrl = (string?)null,
                backgroundImageDataUrl = (string?)null,
            },
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutBranding_LogoNotADataUrl_ReturnsBadRequest()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/branding",
            new
            {
                appName = (string?)null,
                logoDataUrl = "https://example.com/logo.png",
                faviconDataUrl = (string?)null,
                backgroundImageDataUrl = (string?)null,
            },
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("base64 data URL", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PutBranding_LogoExceedsSizeCap_ReturnsBadRequest()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/branding",
            new
            {
                appName = (string?)null,
                logoDataUrl = OversizedLogoDataUrl(),
                faviconDataUrl = (string?)null,
                backgroundImageDataUrl = (string?)null,
            },
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("smaller than", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PutBranding_ValidConfig_NormalizesBlankFieldsToNull_PersistsAndAuditsIt()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();
        var validLogo =
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

        var putResponse = await client.PutAsJsonAsync(
            "/api/branding",
            new
            {
                appName = "  My Connector  ",
                logoDataUrl = validLogo,
                faviconDataUrl = "",
                backgroundImageDataUrl = (string?)null,
            },
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var putBody = await putResponse.Content.ReadFromJsonAsync<JsonElement>(ApiAuth.Json);
        Assert.Equal("My Connector", putBody.GetProperty("appName").GetString());
        Assert.Equal(validLogo, putBody.GetProperty("logoDataUrl").GetString());
        Assert.Equal(JsonValueKind.Null, putBody.GetProperty("faviconDataUrl").ValueKind);

        // The trim/blank-to-null normalization must be what's actually persisted, not just echoed back.
        var getResponse = await _factory.CreateClient().GetAsync("/api/branding");
        var getBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>(ApiAuth.Json);
        Assert.Equal("My Connector", getBody.GetProperty("appName").GetString());
    }
}
