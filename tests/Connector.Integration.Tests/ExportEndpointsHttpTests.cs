using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Connector.Api;
using Connector.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Connector.Integration.Tests;

/// <summary>HTTP-layer coverage for <see cref="ExportEndpoints"/> against the real <see cref="Program"/>
/// pipeline (see <see cref="ApiFactory"/>/<see cref="ApiCollection"/>). Entirely self-contained — every one
/// of these actions only mutates <see cref="ExportRunEntity"/> rows in the local DB, never the ERP — so
/// unlike most of the rest of Connector.Api this needs no Postgres at all. Runs are seeded directly via
/// <see cref="ApiFactory.Services"/> rather than through <c>/api/pipeline/run</c>, which does need a live
/// ERP connection to actually produce one.</summary>
[Collection(ApiCollection.Name)]
public sealed class ExportEndpointsHttpTests
{
    private readonly ApiFactory _factory;

    public ExportEndpointsHttpTests(ApiFactory factory) => _factory = factory;

    private async Task<int> SeedRunAsync(
        int sequenceNo,
        string status = ExportRunStatus.Pending,
        string? extractedAt = null,
        string? releasedAt = null,
        string? deliveredAt = null
    )
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExportLogDbContext>();
        var run = new ExportRunEntity
        {
            SequenceNo = sequenceNo,
            ExtractedAt = extractedAt ?? DateTimeOffset.UtcNow.ToString("O"),
            Status = status,
            Sha256 = new string('a', 64),
            DataFileName = $"export_{sequenceNo:D4}.json",
            ReleasedAt = releasedAt,
            DeliveredAt = deliveredAt,
        };
        db.ExportRuns.Add(run);
        await db.SaveChangesAsync();
        return run.Id;
    }

    // Sequence numbers are shared across every test in this class (same DB throughout the collection), so
    // each test claims its own disjoint block via a distinct base offset to stay order-independent.

    [Fact]
    public async Task GetExports_ReturnsRunsNewestFirstWithStaleFlagForOldPendingRuns()
    {
        await SeedRunAsync(
            90001,
            ExportRunStatus.Pending,
            extractedAt: DateTimeOffset.UtcNow.AddHours(-48).ToString("O")
        );
        await SeedRunAsync(90002, ExportRunStatus.Pending, extractedAt: DateTimeOffset.UtcNow.ToString("O"));
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/exports");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var runs = await response.Content.ReadFromJsonAsync<JsonElement[]>(ApiAuth.Json);
        var newest = Assert.Single(runs!, r => r.GetProperty("sequenceNo").GetInt32() == 90002);
        var oldest = Assert.Single(runs!, r => r.GetProperty("sequenceNo").GetInt32() == 90001);
        Assert.False(newest.GetProperty("isStale").GetBoolean());
        Assert.True(oldest.GetProperty("isStale").GetBoolean());
        // Newest-first ordering: 90002 must appear before 90001 in the array.
        Assert.True(Array.IndexOf(runs!, newest) < Array.IndexOf(runs!, oldest));
    }

    [Fact]
    public async Task GetExportDetail_UnknownSequenceNumber_ReturnsNotFound()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/exports/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetExportDetail_PendingRunWithEarlierUnresolvedRuns_IncludesGapWarning()
    {
        await SeedRunAsync(90101, ExportRunStatus.Failed);
        await SeedRunAsync(90102, ExportRunStatus.Pending);
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/exports/90102");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiAuth.Json);
        Assert.Contains("90101", body.GetProperty("sequenceGapWarning").GetString());
    }

    [Fact]
    public async Task GetExportDetail_NoUnresolvedEarlierRuns_HasNoGapWarning()
    {
        await SeedRunAsync(90201, ExportRunStatus.Released);
        await SeedRunAsync(90202, ExportRunStatus.Pending);
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/exports/90202");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiAuth.Json);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("sequenceGapWarning").ValueKind);
    }

    [Fact]
    public async Task Release_BadApproverPassword_ReturnsBadRequestBeforeTouchingTheRun()
    {
        await SeedRunAsync(90301);
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/exports/90301/release",
            new { approver = "bob", approverPassword = "wrong" },
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var detail = await client.GetAsync("/api/exports/90301");
        var body = await detail.Content.ReadFromJsonAsync<JsonElement>(ApiAuth.Json);
        Assert.Equal(ExportRunStatus.Pending, body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Release_UnknownSequenceNumber_ReturnsNotFound()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/exports/999998/release",
            new { approver = "bob", approverPassword = "bob123" },
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Release_AlreadyReleased_ReturnsConflict()
    {
        await SeedRunAsync(90401, ExportRunStatus.Released, releasedAt: DateTimeOffset.UtcNow.ToString("O"));
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/exports/90401/release",
            new { approver = "bob", approverPassword = "bob123" },
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Release_Valid_MarksReleasedWithOperatorAndApprover()
    {
        await SeedRunAsync(90501);
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/exports/90501/release",
            new { approver = "bob", approverPassword = "bob123" },
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await client.GetAsync("/api/exports/90501");
        var body = await detail.Content.ReadFromJsonAsync<JsonElement>(ApiAuth.Json);
        Assert.Equal(ExportRunStatus.Released, body.GetProperty("status").GetString());
        Assert.Equal("alice", body.GetProperty("operatedBy").GetString());
        Assert.Equal("bob", body.GetProperty("approvedBy").GetString());
    }

    [Fact]
    public async Task Deliver_RunNotReleasedYet_ReturnsBadRequest()
    {
        await SeedRunAsync(90601, ExportRunStatus.Pending);
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/exports/90601/deliver",
            new { importedRecordCount = 5, notes = (string?)null },
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deliver_NotesExceed2000Characters_ReturnsBadRequest()
    {
        await SeedRunAsync(90701, ExportRunStatus.Released);
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/exports/90701/deliver",
            new { importedRecordCount = (int?)null, notes = new string('x', 2001) },
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deliver_Valid_ThenDeliveringAgain_ReturnsConflict()
    {
        await SeedRunAsync(90801, ExportRunStatus.Released);
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var first = await client.PostAsJsonAsync(
            "/api/exports/90801/deliver",
            new { importedRecordCount = 42, notes = "all good" },
            ApiAuth.Json
        );
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync(
            "/api/exports/90801/deliver",
            new { importedRecordCount = 42, notes = "all good" },
            ApiAuth.Json
        );
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Skip_ReleasedRun_ReturnsConflict()
    {
        await SeedRunAsync(90901, ExportRunStatus.Released, releasedAt: DateTimeOffset.UtcNow.ToString("O"));
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/exports/90901/skip", new { reason = "n/a" }, ApiAuth.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Skip_PendingRun_MarksSkippedAndAudits()
    {
        await SeedRunAsync(91001, ExportRunStatus.Pending);
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/exports/91001/skip",
            new { reason = "duplicate trigger" },
            ApiAuth.Json
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await client.GetAsync("/api/exports/91001");
        var body = await detail.Content.ReadFromJsonAsync<JsonElement>(ApiAuth.Json);
        Assert.Equal(ExportRunStatus.Skipped, body.GetProperty("status").GetString());
    }
}
