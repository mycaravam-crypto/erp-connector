using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Connector.Api;
using Connector.Core.DynamicImport;
using Connector.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Connector.Integration.Tests;

/// <summary>HTTP-layer coverage for <see cref="ImportRunEndpoints"/> against the real <see cref="Program"/>
/// pipeline (see <see cref="ApiFactory"/>/<see cref="ApiCollection"/>). As the file's own doc comment says,
/// this layer is only request validation and status-code mapping — the commit logic lives in
/// <see cref="ImportRunReleaser"/>. <see cref="ImportRunReleaser.RejectAsync"/> never touches the ERP, so a
/// full reject round-trip is covered end to end; <see cref="ImportRunReleaser.ReleaseAsync"/> does, but its
/// very first guard (no ERP connection configured, per <c>SettingsKeys.ErpConnection</c>) fires before any
/// Postgres connection is attempted, so that failure path is reachable here without one too.</summary>
[Collection(ApiCollection.Name)]
public sealed class ImportRunEndpointsHttpTests
{
    private readonly ApiFactory _factory;

    public ImportRunEndpointsHttpTests(ApiFactory factory) => _factory = factory;

    private async Task<int> SeedRunAsync(string status = ImportRunStatus.PendingReview, string? planJson = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExportLogDbContext>();
        var definition = new ImportDefinitionEntity
        {
            Name = "CI confirmation",
            RootTable = "systemconfiguration",
            RootMatchColumn = "id",
            RootNode = "{}",
            IsEnabled = true,
            ConfigVersion = 1,
            CreatedBy = "alice",
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
        };
        db.ImportDefinitions.Add(definition);
        await db.SaveChangesAsync();

        var run = new ImportRunEntity
        {
            ImportDefinitionId = definition.Id,
            ConfigVersion = 1,
            SourceFileName = "drop.json",
            Sha256Checksum = Guid.NewGuid().ToString("N").PadRight(64, '0'),
            StartedAt = DateTimeOffset.UtcNow.ToString("O"),
            Status = status,
            PlanJson = planJson,
        };
        db.ImportRuns.Add(run);
        await db.SaveChangesAsync();
        return run.Id;
    }

    [Fact]
    public async Task GetRun_UnknownId_ReturnsNotFound()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/import-runs/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetRun_Existing_ReturnsDetailWithDefinitionName()
    {
        var id = await SeedRunAsync();
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync($"/api/import-runs/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiAuth.Json);
        Assert.Equal("CI confirmation", body.GetProperty("importDefinitionName").GetString());
        Assert.Equal(ImportRunStatus.PendingReview, body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Release_UnknownId_ReturnsNotFound()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/import-runs/999999/release",
            new { approver = "bob", approverPassword = "bob123" },
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Release_BadApproverPassword_ReturnsBadRequestBeforeTouchingTheRun()
    {
        var id = await SeedRunAsync();
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/import-runs/{id}/release",
            new { approver = "bob", approverPassword = "wrong" },
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var detail = await client.GetAsync($"/api/import-runs/{id}");
        var body = await detail.Content.ReadFromJsonAsync<JsonElement>(ApiAuth.Json);
        Assert.Equal(ImportRunStatus.PendingReview, body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Release_SelfApproval_ReturnsBadRequest()
    {
        var id = await SeedRunAsync();
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/import-runs/{id}/release",
            new { approver = "alice", approverPassword = "alice123" },
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Release_RunNotPendingReview_ReturnsConflict()
    {
        var id = await SeedRunAsync(ImportRunStatus.Rejected);
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/import-runs/{id}/release",
            new { approver = "bob", approverPassword = "bob123" },
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // No AppSetting for the ERP connection has been stored anywhere in this collection's shared DB, so
    // ImportRunReleaser.ReleaseAsync's very first guard fires: this is the one "successful validation,
    // release attempted" path reachable without a live Postgres instance.
    [Fact]
    public async Task Release_ValidApprovalButNoDataSourceConfigured_MarksRunFailedAndReturns500()
    {
        // ImportPlanJson.Serialize/Deserialize both call JsonSerializer with no options, i.e. exact
        // (PascalCase) member names — matching that here rather than passing a raw JSON literal.
        var plan = new ImportPlan(0, 0, 0, 0, 0, 0, []);
        var id = await SeedRunAsync(planJson: ImportPlanJson.Serialize(plan));
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/import-runs/{id}/release",
            new { approver = "bob", approverPassword = "bob123" },
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var detail = await client.GetAsync($"/api/import-runs/{id}");
        var body = await detail.Content.ReadFromJsonAsync<JsonElement>(ApiAuth.Json);
        Assert.Equal(ImportRunStatus.Failed, body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Reject_UnknownId_ReturnsNotFound()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync("/api/import-runs/999999/reject", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reject_RunNotPendingReview_ReturnsConflict()
    {
        var id = await SeedRunAsync(ImportRunStatus.Released);
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync($"/api/import-runs/{id}/reject", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Reject_PendingReviewRun_MarksRejectedWithoutNeedingAnApprover()
    {
        var id = await SeedRunAsync();
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync($"/api/import-runs/{id}/reject", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await client.GetAsync($"/api/import-runs/{id}");
        var body = await detail.Content.ReadFromJsonAsync<JsonElement>(ApiAuth.Json);
        Assert.Equal(ImportRunStatus.Rejected, body.GetProperty("status").GetString());
        Assert.Equal("alice", body.GetProperty("operatedBy").GetString());
    }
}
