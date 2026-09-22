using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Connector.Api;
using Connector.Core.DynamicExport;
using Connector.Infrastructure;

namespace Connector.Integration.Tests;

/// <summary>HTTP-layer coverage for <see cref="ExportMappingEndpoints"/> against the real
/// <see cref="Program"/> pipeline (see <see cref="ApiFactory"/>/<see cref="ApiCollection"/>) — entirely
/// self-contained (everything lives in the local <c>AppSetting</c> store, no ERP connection needed), and
/// almost the whole file is <c>ValidateConfigAsync</c>'s branches, previously untested.</summary>
[Collection(ApiCollection.Name)]
public sealed class ExportMappingEndpointsHttpTests
{
    private readonly ApiFactory _factory;

    public ExportMappingEndpointsHttpTests(ApiFactory factory) => _factory = factory;

    private static ExportMappingConfig MinimalValidConfig(string sourceTable = "systemconfiguration") =>
        new(sourceTable, [new ExportMappingField("id", "id", true)], []);

    private static ExportMappingNestedGroup NestedGroupChain(int depth)
    {
        var leaf = new ExportMappingNestedGroup(
            "level0",
            "related_table",
            "id",
            "id",
            true,
            "object",
            [new ExportMappingNestedField("name", "name", true)],
            []
        );
        var current = leaf;
        for (var i = 1; i < depth; i++)
            current = new ExportMappingNestedGroup(
                $"level{i}",
                "related_table",
                "id",
                "id",
                true,
                "object",
                [],
                [current]
            );
        return current;
    }

    [Fact]
    public async Task PutExportMapping_MissingSourceTable_ReturnsBadRequest()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/export-mapping",
            MinimalValidConfig(sourceTable: ""),
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("SourceTable is required", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PutExportMapping_EnabledFieldMissingTargetName_ReturnsBadRequest()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();
        var config = new ExportMappingConfig("systemconfiguration", [new ExportMappingField("id", "", true)], []);

        var response = await client.PutAsJsonAsync("/api/export-mapping", config, ApiAuth.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutExportMapping_GdprDeniedFieldEnabled_ReturnsBadRequest()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();
        var config = new ExportMappingConfig(
            "systemconfiguration",
            [new ExportMappingField("technician_name", "tech", true)],
            []
        );

        var response = await client.PutAsJsonAsync("/api/export-mapping", config, ApiAuth.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("GDPR violation", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PutExportMapping_EnabledRelationMissingJoinKey_ReturnsBadRequest()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();
        var config = new ExportMappingConfig(
            "systemconfiguration",
            [],
            [new ExportMappingRelation("addresses", "id", "", true, "string_join", ", ", [new("city", "city", true)])]
        );

        var response = await client.PutAsJsonAsync("/api/export-mapping", config, ApiAuth.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutExportMapping_NestedGroupInvalidKind_ReturnsBadRequest()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();
        var badGroup = new ExportMappingNestedGroup(
            "addr",
            "addresses",
            "id",
            "id",
            true,
            "not-object-or-array",
            [new ExportMappingNestedField("city", "city", true)],
            []
        );
        var config = new ExportMappingConfig("systemconfiguration", [], [], NestedGroups: [badGroup]);

        var response = await client.PutAsJsonAsync("/api/export-mapping", config, ApiAuth.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Kind", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PutExportMapping_NestedGroupRelatedTableNotAnIdentifier_ReturnsBadRequest()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();
        var badGroup = new ExportMappingNestedGroup(
            "addr",
            "addresses; DROP TABLE users",
            "id",
            "id",
            true,
            "object",
            [new ExportMappingNestedField("city", "city", true)],
            []
        );
        var config = new ExportMappingConfig("systemconfiguration", [], [], NestedGroups: [badGroup]);

        var response = await client.PutAsJsonAsync("/api/export-mapping", config, ApiAuth.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutExportMapping_NestedGroupExceedsMaxDepth_ReturnsBadRequest()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();
        var tooDeep = NestedGroupChain(DynamicExportService.MaxNestedDepth + 1);
        var config = new ExportMappingConfig("systemconfiguration", [], [], NestedGroups: [tooDeep]);

        var response = await client.PutAsJsonAsync("/api/export-mapping", config, ApiAuth.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("maximum nesting depth", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PutExportMapping_DuplicateTopLevelExportKey_ReturnsBadRequest()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();
        var config = new ExportMappingConfig(
            "systemconfiguration",
            [new ExportMappingField("id", "dup", true), new ExportMappingField("guid", "dup", true)],
            []
        );

        var response = await client.PutAsJsonAsync("/api/export-mapping", config, ApiAuth.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Duplicate export key", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetExportMapping_ThenPutValid_NotFoundBeforeAndReflectedAfter()
    {
        // Actively arranged rather than assumed: every test class in ApiCollection shares one DB, and
        // PipelineEndpointsHttpTests's guard-path tests also set/clear this same key.
        await _factory.ClearAsync(SettingsKeys.ExportMapping);
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var beforeResponse = await client.GetAsync("/api/export-mapping");
        Assert.Equal(HttpStatusCode.NotFound, beforeResponse.StatusCode);

        var putResponse = await client.PutAsJsonAsync(
            "/api/export-mapping",
            MinimalValidConfig("systemconfiguration"),
            ApiAuth.Json
        );
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var getResponse = await client.GetAsync("/api/export-mapping");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>(ApiAuth.Json);
        Assert.Equal("systemconfiguration", body.GetProperty("sourceTable").GetString());
    }

    [Fact]
    public async Task Presets_FullLifecycle_PutListDeleteNotFoundAfter()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();
        var presetName = "test-preset-" + Guid.NewGuid().ToString("N")[..8];

        var putResponse = await client.PutAsJsonAsync(
            $"/api/export-mapping/presets/{presetName}",
            MinimalValidConfig("masterdata"),
            ApiAuth.Json
        );
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var listResponse = await client.GetAsync("/api/export-mapping/presets");
        var presets = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ApiAuth.Json);
        Assert.Equal("masterdata", presets.GetProperty(presetName).GetProperty("sourceTable").GetString());

        var deleteResponse = await client.DeleteAsync($"/api/export-mapping/presets/{presetName}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var secondDelete = await client.DeleteAsync($"/api/export-mapping/presets/{presetName}");
        Assert.Equal(HttpStatusCode.NotFound, secondDelete.StatusCode);
    }

    [Fact]
    public async Task Presets_PutWithInvalidConfig_ReturnsBadRequestWithoutSavingAnything()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();
        var invalidConfig = new ExportMappingConfig("", [], []);

        var response = await client.PutAsJsonAsync(
            "/api/export-mapping/presets/bad-preset",
            invalidConfig,
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var listResponse = await client.GetAsync("/api/export-mapping/presets");
        var presets = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ApiAuth.Json);
        Assert.False(presets.TryGetProperty("bad-preset", out _));
    }

    [Fact]
    public async Task DeletePreset_UnknownName_ReturnsNotFound()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync("/api/export-mapping/presets/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
