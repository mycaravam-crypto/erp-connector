using Connector.Core.DynamicImport;
using Connector.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Connector.Integration.Tests;

/// <summary>
/// Real-Postgres coverage for <see cref="ImportRunReleaser"/> — Slice 3 of Phase 17 (import-definitions.md
/// §3 steps 6-8), the commit-path counterpart to <see cref="ImportNodeWalkerPostgresTests"/>'s read-only
/// walk coverage. Uses the same local <c>testdb</c> fixture and the same "no-op instead of fail" convention
/// when it isn't running (see that class's doc comment for why). Unlike that class, these tests actually
/// write to <c>testdb</c> — always to the three <c>c000000...</c> <c>systemconfiguration</c> rows
/// <c>testdb/init.sql</c> reserves for exactly this, restored to their seeded value in a <c>finally</c> block
/// regardless of test outcome so a failed assertion never leaves a mutated row for the next run.
///
/// Requires: <c>docker-compose --profile test up -d testdb</c>.
/// </summary>
public sealed class ImportRunReleaserPostgresTests
{
    // Seeded in testdb/init.sql, reserved for this test class — see its own comment there.
    private const string FixtureA = "c0000001-0001-0001-0001-000000000001";
    private const string FixtureB = "c0000002-0002-0002-0002-000000000002";
    private const string FixtureC = "c0000003-0003-0003-0003-000000000003";

    private static async Task<string?> ReadStatusAsync(NpgsqlConnection conn, string ciId)
    {
        await using var cmd = new NpgsqlCommand("SELECT status FROM systemconfiguration WHERE id::text = @id", conn);
        cmd.Parameters.AddWithValue("id", ciId);
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    private static async Task ResetStatusAsync(NpgsqlConnection conn, string ciId, string status = "active")
    {
        await using var cmd = new NpgsqlCommand(
            "UPDATE systemconfiguration SET status = @status WHERE id::text = @id",
            conn
        );
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("id", ciId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<ImportRunEntity> SeedRunAsync(ExportLogDbContext db, ImportPlan plan, string checksum)
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
        db.ImportDefinitions.Add(definition);
        await db.SaveChangesAsync();

        var run = new ImportRunEntity
        {
            ImportDefinitionId = definition.Id,
            ConfigVersion = 1,
            SourceFileName = "vendor-drop.json",
            Sha256Checksum = checksum,
            StartedAt = "2026-01-01T00:00:00Z",
            Status = ImportRunStatus.PendingReview,
            TriggeredBy = "watcher",
            RecordCount = plan.RecordCount,
            MatchedCount = plan.MatchedCount,
            ChangedCount = plan.ChangedCount,
            UnchangedCount = plan.UnchangedCount,
            RejectedCount = plan.RejectedCount,
            InvalidCount = plan.InvalidCount,
            PlanJson = ImportPlanJson.Serialize(plan),
        };
        db.ImportRuns.Add(run);
        await db.SaveChangesAsync();
        return run;
    }

    private static ImportPlan SingleOperationPlan(string ciId, string column, string? expectedOld, string newValue) =>
        new(
            RecordCount: 1,
            MatchedCount: 1,
            ChangedCount: 1,
            UnchangedCount: 0,
            RejectedCount: 0,
            InvalidCount: 0,
            Operations:
            [
                new ImportPlanOperation(ciId, "systemconfiguration", "id", ciId, column, expectedOld, newValue),
            ]
        );

    [Fact]
    public async Task ReleaseAsync_ExpectedOldValueStillMatches_CommitsAndMarksReleased()
    {
        await using var erp = await ErpTestFixture.TryOpenAsync();
        if (erp is null)
            return;

        await using var local = await LocalDb.NewAsync();
        var db = local.Db;
        var audit = new AuditService(db, NullLogger<AuditService>.Instance);

        try
        {
            var plan = SingleOperationPlan(FixtureA, "status", "active", "confirmed");
            var run = await SeedRunAsync(db, plan, new string('a', 64));

            await ImportRunReleaser.ReleaseAsync(db, run, "alice", "bob", audit, CancellationToken.None);

            Assert.Equal(ImportRunStatus.Released, run.Status);
            Assert.Equal(0, run.ConflictCount);
            Assert.Equal("alice", run.OperatedBy);
            Assert.Equal("bob", run.ApprovedBy);
            Assert.NotNull(run.ReleasedAt);

            Assert.Equal("confirmed", await ReadStatusAsync(erp, FixtureA));

            var auditEntry = Assert.Single(db.AuditLog.Where(a => a.Action == "import_run_released"));
            Assert.Equal("alice", auditEntry.Username);
            Assert.Contains("bob", auditEntry.Detail);
        }
        finally
        {
            await ResetStatusAsync(erp, FixtureA);
        }
    }

    [Fact]
    public async Task ReleaseAsync_RowChangedSinceStaging_ExcludesRowAsConflictedWithoutOverwriting()
    {
        await using var erp = await ErpTestFixture.TryOpenAsync();
        if (erp is null)
            return;

        await using var local = await LocalDb.NewAsync();
        var db = local.Db;
        var audit = new AuditService(db, NullLogger<AuditService>.Instance);

        try
        {
            // The plan was built against "active", but the ERP row has since moved on to "decommissioned" —
            // simulating another process updating it while this run sat in PendingReview.
            await ResetStatusAsync(erp, FixtureB, "decommissioned");
            var plan = SingleOperationPlan(FixtureB, "status", "active", "confirmed");
            var run = await SeedRunAsync(db, plan, new string('b', 64));

            await ImportRunReleaser.ReleaseAsync(db, run, "alice", "bob", audit, CancellationToken.None);

            // Still Released, not Failed: a conflicted row doesn't fail the run (Open Decision #6).
            Assert.Equal(ImportRunStatus.Released, run.Status);
            Assert.Equal(1, run.ConflictCount);

            // Not overwritten — the newer value survives.
            Assert.Equal("decommissioned", await ReadStatusAsync(erp, FixtureB));
        }
        finally
        {
            await ResetStatusAsync(erp, FixtureB);
        }
    }

    [Fact]
    public async Task ReleaseAsync_UnrelatedFailureMidCommit_RollsBackEverythingAndMarksFailed()
    {
        await using var erp = await ErpTestFixture.TryOpenAsync();
        if (erp is null)
            return;

        await using var local = await LocalDb.NewAsync();
        var db = local.Db;
        var audit = new AuditService(db, NullLogger<AuditService>.Instance);

        try
        {
            // Two operations in one plan: FixtureC's would succeed on its own, but "no_such_column" doesn't
            // exist on systemconfiguration, so its UPDATE throws — the whole transaction must roll back,
            // including FixtureC's otherwise-valid change.
            var plan = new ImportPlan(
                RecordCount: 2,
                MatchedCount: 2,
                ChangedCount: 2,
                UnchangedCount: 0,
                RejectedCount: 0,
                InvalidCount: 0,
                Operations:
                [
                    new ImportPlanOperation(
                        FixtureC,
                        "systemconfiguration",
                        "id",
                        FixtureC,
                        "status",
                        "active",
                        "confirmed"
                    ),
                    new ImportPlanOperation(
                        "bogus",
                        "systemconfiguration",
                        "id",
                        "00000000-0000-0000-0000-000000000000",
                        "no_such_column",
                        null,
                        "x"
                    ),
                ]
            );
            var run = await SeedRunAsync(db, plan, new string('c', 64));

            await ImportRunReleaser.ReleaseAsync(db, run, "alice", "bob", audit, CancellationToken.None);

            Assert.Equal(ImportRunStatus.Failed, run.Status);
            Assert.NotNull(run.ErrorMessage);

            // FixtureC's change never committed — the whole transaction rolled back.
            Assert.Equal("active", await ReadStatusAsync(erp, FixtureC));

            var auditEntry = Assert.Single(db.AuditLog.Where(a => a.Action == "import_run_failed"));
            Assert.Equal("alice", auditEntry.Username);
        }
        finally
        {
            await ResetStatusAsync(erp, FixtureC);
        }
    }

    [Fact]
    public async Task RejectAsync_MarksRejectedWithoutTouchingErpOrRequiringApprover()
    {
        await using var local = await LocalDb.NewAsync();
        var db = local.Db;
        var audit = new AuditService(db, NullLogger<AuditService>.Instance);

        var plan = SingleOperationPlan(FixtureA, "status", "active", "confirmed");
        var run = await SeedRunAsync(db, plan, new string('d', 64));

        await ImportRunReleaser.RejectAsync(db, run, "alice", audit, CancellationToken.None);

        Assert.Equal(ImportRunStatus.Rejected, run.Status);
        Assert.Equal("alice", run.OperatedBy);
        Assert.Null(run.ApprovedBy);

        var auditEntry = Assert.Single(db.AuditLog.Where(a => a.Action == "import_run_rejected"));
        Assert.Equal("alice", auditEntry.Username);
    }
}
