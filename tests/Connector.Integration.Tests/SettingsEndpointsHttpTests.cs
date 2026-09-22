using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Connector.Api;

namespace Connector.Integration.Tests;

/// <summary>HTTP-layer coverage for <see cref="SettingsEndpoints"/> against the real <see cref="Program"/>
/// pipeline (see <see cref="ApiFactory"/>/<see cref="ApiCollection"/>): scheduler config validation, the
/// GDPR denylist, and the audit log — none of which touch the ERP connection, so nothing here needs
/// Postgres.</summary>
[Collection(ApiCollection.Name)]
public sealed class SettingsEndpointsHttpTests
{
    private readonly ApiFactory _factory;

    public SettingsEndpointsHttpTests(ApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("not-a-time", 30, "json")]
    [InlineData("25:00", 30, "json")]
    [InlineData("06:00", 0, "json")]
    [InlineData("06:00", 3651, "json")]
    [InlineData("06:00", 30, "yaml")]
    public async Task PutScheduler_InvalidField_ReturnsBadRequest(string time, int retentionDays, string format)
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/settings/scheduler",
            new
            {
                scheduledTimeUtc = time,
                retentionDays,
                format,
            },
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutScheduler_Valid_PersistsAndIsReflectedByGetAndByAudit()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();
        // A distinctive time makes this test's own audit entry unambiguous even though the audit log is
        // shared with every other test in this collection.
        var putResponse = await client.PutAsJsonAsync(
            "/api/settings/scheduler",
            new
            {
                scheduledTimeUtc = "03:17",
                retentionDays = 45,
                format = "csv",
            },
            ApiAuth.Json
        );
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var getResponse = await client.GetAsync("/api/settings/scheduler");
        var getBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>(ApiAuth.Json);
        Assert.Equal("03:17", getBody.GetProperty("scheduledTimeUtc").GetString());
        Assert.Equal(45, getBody.GetProperty("retentionDays").GetInt32());
        Assert.Equal("csv", getBody.GetProperty("format").GetString());

        var auditResponse = await client.GetAsync("/api/audit?limit=1");
        var auditBody = await auditResponse.Content.ReadFromJsonAsync<JsonElement[]>(ApiAuth.Json);
        var newest = Assert.Single(auditBody!);
        Assert.Equal("scheduler_updated", newest.GetProperty("action").GetString());
        Assert.Contains("time=03:17", newest.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task GetGdprDeniedFields_ReturnsAWellFormedNonEmptyList()
    {
        // Doesn't assert the specific hardcoded defaults here: this class's own
        // PatchGdprDeniedFields_Valid test replaces the stored denylist, and xUnit doesn't guarantee
        // execution order between test methods in the same class, let alone the same collection. The
        // "falls back to the hardcoded defaults when nothing is stored" branch is covered directly against
        // a known-empty LocalDb instead (order-independent) rather than through this shared-state HTTP path.
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/gdpr-denied-fields");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiAuth.Json);
        var fields = body.GetProperty("fields").EnumerateArray().Select(f => f.GetString()).ToList();
        Assert.NotEmpty(fields);
    }

    [Fact]
    public async Task PatchGdprDeniedFields_EmptyList_ReturnsBadRequest()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PatchAsync(
            "/api/gdpr-denied-fields",
            JsonContent.Create(new { fields = Array.Empty<string>() }, options: ApiAuth.Json)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchGdprDeniedFields_MoreThanFifty_ReturnsBadRequest()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PatchAsync(
            "/api/gdpr-denied-fields",
            JsonContent.Create(
                new { fields = Enumerable.Range(0, 51).Select(i => $"field_{i}") },
                options: ApiAuth.Json
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchGdprDeniedFields_Valid_ReplacesTheDenylistAndGetReflectsIt()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var patchResponse = await client.PatchAsync(
            "/api/gdpr-denied-fields",
            JsonContent.Create(new { fields = new[] { "custom_field_one", "custom_field_two" } }, options: ApiAuth.Json)
        );
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);

        var getResponse = await client.GetAsync("/api/gdpr-denied-fields");
        var getBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>(ApiAuth.Json);
        var fields = getBody.GetProperty("fields").EnumerateArray().Select(f => f.GetString()).ToList();
        Assert.Equal(["custom_field_one", "custom_field_two"], fields);
    }

    [Fact]
    public async Task GetAudit_LimitParameter_CapsTheNumberOfEntriesReturned()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();
        // By this point in the collection, other tests have already produced several audit entries.
        await client.PutAsJsonAsync(
            "/api/settings/scheduler",
            new
            {
                scheduledTimeUtc = "09:41",
                retentionDays = 10,
                format = "json",
            },
            ApiAuth.Json
        );

        var response = await client.GetAsync("/api/audit?limit=1");

        var body = await response.Content.ReadFromJsonAsync<JsonElement[]>(ApiAuth.Json);
        Assert.Single(body!);
    }
}
