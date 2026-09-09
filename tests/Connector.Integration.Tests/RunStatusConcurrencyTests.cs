using Connector.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Connector.Integration.Tests;

/// <summary>
/// Coverage for the optimistic-concurrency guard on <see cref="ExportRunEntity.Status"/> and
/// <see cref="ImportRunEntity.Status"/> (security audit finding: two concurrent release/skip/deliver
/// requests against the same run could both pass their "is this still Pending?" check before either
/// committed, letting the second silently overwrite the first's Operator/Approver/ReleasedAt). Each test
/// uses two separate <see cref="ExportLogDbContext"/> instances sharing one in-memory Sqlite connection —
/// mirroring two concurrent HTTP requests, each with its own scoped DbContext, exactly as ASP.NET Core
/// would hand out in production.
/// </summary>
public sealed class RunStatusConcurrencyTests
{
    private static async Task<(
        SqliteConnection Connection,
        DbContextOptions<ExportLogDbContext> Options
    )> NewSharedDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ExportLogDbContext>().UseSqlite(connection).Options;
        await using (var setup = new ExportLogDbContext(options, new EphemeralDataProtectionProvider()))
            await setup.Database.EnsureCreatedAsync();
        return (connection, options);
    }

    [Fact]
    public async Task ExportRun_TwoConcurrentWriters_SecondThrowsConcurrencyException()
    {
        var (connection, options) = await NewSharedDbAsync();
        await using var _ = connection;

        await using (var seed = new ExportLogDbContext(options, new EphemeralDataProtectionProvider()))
        {
            seed.ExportRuns.Add(
                new ExportRunEntity
                {
                    SequenceNo = 1,
                    Status = ExportRunStatus.Pending,
                    ExtractedAt = "2026-01-01T00:00:00Z",
                }
            );
            await seed.SaveChangesAsync();
        }

        // Two independent scoped contexts, each loading the same row — simulates two concurrent requests.
        await using var dbA = new ExportLogDbContext(options, new EphemeralDataProtectionProvider());
        await using var dbB = new ExportLogDbContext(options, new EphemeralDataProtectionProvider());
        var runA = await dbA.ExportRuns.FirstAsync(r => r.SequenceNo == 1);
        var runB = await dbB.ExportRuns.FirstAsync(r => r.SequenceNo == 1);

        runA.Status = ExportRunStatus.Released;
        await dbA.SaveChangesAsync(); // first writer wins

        runB.Status = ExportRunStatus.Skipped;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());

        await using var verify = new ExportLogDbContext(options, new EphemeralDataProtectionProvider());
        var final = await verify.ExportRuns.FirstAsync(r => r.SequenceNo == 1);
        Assert.Equal(ExportRunStatus.Released, final.Status);
    }

    [Fact]
    public async Task ImportRun_TwoConcurrentWriters_SecondThrowsConcurrencyException()
    {
        var (connection, options) = await NewSharedDbAsync();
        await using var _ = connection;

        int runId;
        await using (var seed = new ExportLogDbContext(options, new EphemeralDataProtectionProvider()))
        {
            var definition = new ImportDefinitionEntity
            {
                Name = "Test Import Definition",
                RootTable = "systemconfiguration",
                RootMatchColumn = "id",
                RootNode = "{}",
                AllowedWritableColumns = """["status"]""",
                IsEnabled = true,
                ConfigVersion = 1,
                CreatedBy = "test",
                CreatedAt = "2026-01-01T00:00:00Z",
            };
            seed.ImportDefinitions.Add(definition);
            await seed.SaveChangesAsync();

            var run = new ImportRunEntity
            {
                ImportDefinitionId = definition.Id,
                ConfigVersion = 1,
                SourceFileName = "vendor-drop.json",
                Sha256Checksum = new string('a', 64),
                StartedAt = "2026-01-01T00:00:00Z",
                Status = ImportRunStatus.PendingReview,
                TriggeredBy = "watcher",
            };
            seed.ImportRuns.Add(run);
            await seed.SaveChangesAsync();
            runId = run.Id;
        }

        await using var dbA = new ExportLogDbContext(options, new EphemeralDataProtectionProvider());
        await using var dbB = new ExportLogDbContext(options, new EphemeralDataProtectionProvider());
        var runA = await dbA.ImportRuns.FirstAsync(r => r.Id == runId);
        var runB = await dbB.ImportRuns.FirstAsync(r => r.Id == runId);

        runA.Status = ImportRunStatus.Released;
        await dbA.SaveChangesAsync(); // first writer wins

        runB.Status = ImportRunStatus.Rejected;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());

        await using var verify = new ExportLogDbContext(options, new EphemeralDataProtectionProvider());
        var final = await verify.ImportRuns.FirstAsync(r => r.Id == runId);
        Assert.Equal(ImportRunStatus.Released, final.Status);
    }
}
