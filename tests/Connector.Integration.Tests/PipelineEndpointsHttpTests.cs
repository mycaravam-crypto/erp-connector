using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Connector.Api;
using Connector.Core.DynamicExport;
using Connector.Infrastructure;

namespace Connector.Integration.Tests;

/// <summary>HTTP-layer coverage for <see cref="PipelineEndpoints"/> against the real <see cref="Program"/>
/// pipeline (see <see cref="ApiFactory"/>/<see cref="ApiCollection"/>). Every action here eventually opens
/// a live Postgres connection to the ERP, which this sandbox doesn't have — but each one validates
/// configuration (export mapping present, ERP connection present) *before* attempting that connection, so
/// those guard paths, and the SR-07 ApiKey-only auth requirement on the named-preset route, are reachable
/// without one. <see cref="ApiSettings"/> actively arranges each test's AppSetting preconditions rather
/// than assuming them, since every class in the collection shares one DB.</summary>
[Collection(ApiCollection.Name)]
public sealed class PipelineEndpointsHttpTests
{
    private readonly ApiFactory _factory;

    public PipelineEndpointsHttpTests(ApiFactory factory) => _factory = factory;

    private static ExportMappingConfig MinimalValidConfig(string sourceTable = "systemconfiguration") =>
        new(sourceTable, [new ExportMappingField("id", "id", true)], []);

    [Fact]
    public async Task Run_NoExportMappingConfigured_ReturnsBadRequest()
    {
        await _factory.ClearAsync(SettingsKeys.ExportMapping);
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync("/api/pipeline/run", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("No export mapping configured", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Run_MappingConfiguredButNoErpConnection_ReturnsBadRequest()
    {
        await _factory.SetAsync(SettingsKeys.ExportMapping, MinimalValidConfig());
        await _factory.ClearAsync(SettingsKeys.ErpConnection);
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync("/api/pipeline/run", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("No database connection configured", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RunPreset_UnknownName_ReturnsNotFound()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "dev-local-api-key");

        var response = await client.PostAsync($"/api/pipeline/run/no-such-preset-{Guid.NewGuid():N}", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RunPreset_KnownPresetButNoErpConnection_ReturnsBadRequest()
    {
        var presetName = "test-preset-" + Guid.NewGuid().ToString("N")[..8];
        await _factory.SetAsync(
            SettingsKeys.ExportPresets,
            new Dictionary<string, ExportMappingConfig> { [presetName] = MinimalValidConfig() }
        );
        await _factory.ClearAsync(SettingsKeys.ErpConnection);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "dev-local-api-key");

        var response = await client.PostAsync($"/api/pipeline/run/{presetName}", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("No database connection configured", await response.Content.ReadAsStringAsync());
    }

    // SR-07: a normal logged-in user's JWT must not authenticate this route — only a configured API key
    // (machine-to-machine caller) may, precisely so an interactive session can't bypass four-eyes review
    // via the named-preset route.
    [Fact]
    public async Task RunPreset_JwtBearerWithoutApiKey_ReturnsUnauthorized()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync($"/api/pipeline/run/whatever-{Guid.NewGuid():N}", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RunPreset_NoAuthAtAll_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync($"/api/pipeline/run/whatever-{Guid.NewGuid():N}", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Preview_NoExportMappingConfigured_ReturnsOkWithErrorMessage()
    {
        await _factory.ClearAsync(SettingsKeys.ExportMapping);
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/pipeline/preview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiAuth.Json);
        Assert.Equal("error", body.GetProperty("source").GetString());
        Assert.Contains("No export mapping configured", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Preview_MappingConfiguredButNoErpConnection_ReturnsOkWithErrorMessage()
    {
        await _factory.SetAsync(SettingsKeys.ExportMapping, MinimalValidConfig());
        await _factory.ClearAsync(SettingsKeys.ErpConnection);
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/pipeline/preview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiAuth.Json);
        Assert.Equal("error", body.GetProperty("source").GetString());
        Assert.Contains("No database connection configured", body.GetProperty("error").GetString());
    }
}
