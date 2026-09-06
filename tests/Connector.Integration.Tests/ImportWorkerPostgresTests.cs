using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Connector.Core.DynamicImport;
using Connector.Core.Schema;
using Connector.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Connector.Integration.Tests;

/// <summary>
/// Real-Postgres, real-filesystem coverage for <see cref="ImportWorker"/> — Slice 4 of Phase 17
/// (import-definitions.md §3 steps 1-2, §5), the folder-watcher counterpart to
/// <see cref="ImportNodeWalkerPostgresTests"/>'s read-only walk coverage and
/// <see cref="ImportRunReleaserPostgresTests"/>'s commit-path coverage. Uses the same local <c>testdb</c>
/// fixture and the same "no-op instead of fail" convention when it isn't running (see
/// <see cref="ImportNodeWalkerPostgresTests"/>'s doc comment for why). Every test here only ever reads
/// <c>testdb</c> (staging never writes to the ERP), so it freely reuses <c>ImportNodeWalkerPostgresTests</c>'
/// read-only fixture rows rather than needing its own reserved set.
///
/// Requires: <c>docker-compose --profile test up -d testdb</c>.
/// </summary>
public sealed class ImportWorkerPostgresTests
{
    // Seeded in testdb/init.sql: status=active, storage_location='Bay 7'. Read-only for this test class.
    // (technician_name is deliberately not used here — it's on DynamicExportService.GdprDeniedFields, so
    // it's rejected outright as a writable target regardless of AllowedWritableColumns.)
    private const string FixtureCiId = "55555555-5555-5555-5555-555555555555";

    // Hands ImportWorker a fixed pair of already-constructed services, standing in for the DI scope it
    // would normally get from IServiceScopeFactory in Program.cs — avoids standing up a real ServiceCollection
    // just to serve two singletons for the duration of one test.
    private sealed class FixedServiceScopeFactory(IServiceProvider provider) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new FixedScope(provider);

        private sealed class FixedScope(IServiceProvider provider) : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; } = provider;

            public void Dispose() { }
        }
    }

    private sealed class FixedServiceProvider(ExportLogDbContext db, AuditService audit) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ExportLogDbContext))
                return db;
            if (serviceType == typeof(AuditService))
                return audit;
            return null;
        }
    }

    private static ImportNode Scalar(string sourceKey, string targetColumn) =>
        new(
            SourceKey: sourceKey,
            Kind: ImportNodeKind.ScalarField,
            TargetColumn: targetColumn,
            RelatedTable: null,
            JoinKey: null,
            SourceJoinKey: null,
            OnMissingChild: OnMissingChildPolicy.Reject,
            Mapping: null,
            Children: [],
            Enabled: true
        );

    private static ImportNode Root() =>
        new(
            SourceKey: "root",
            Kind: ImportNodeKind.Root,
            TargetColumn: null,
            RelatedTable: null,
            JoinKey: null,
            SourceJoinKey: null,
            OnMissingChild: OnMissingChildPolicy.Reject,
            Mapping: null,
            Children: [Scalar("ciId", "id"), Scalar("storageLocation", "storage_location")],
            Enabled: true
        );

    private const string DefinitionName = "Test Import Definition";

    private static async Task<ImportDefinitionEntity> SeedDefinitionAsync(ExportLogDbContext db, bool isEnabled = true)
    {
        var definition = new ImportDefinitionEntity
        {
            Name = DefinitionName,
            RootTable = "systemconfiguration",
            RootMatchColumn = "id",
            RootNode = ImportNodeJson.Serialize(Root()),
            AllowedWritableColumns = """["storage_location"]""",
            IsEnabled = isEnabled,
            ConfigVersion = 1,
            CreatedBy = "test",
            CreatedAt = "2026-01-01T00:00:00Z",
        };
        db.ImportDefinitions.Add(definition);
        await db.SaveChangesAsync();
        return definition;
    }

    private static ImportWorker NewWorker(ExportLogDbContext db, string inboundPath)
    {
        var audit = new AuditService(db, NullLogger<AuditService>.Instance);
        var scopeFactory = new FixedServiceScopeFactory(new FixedServiceProvider(db, audit));
        return new ImportWorker(
            scopeFactory,
            Options.Create(new ImportSinkOptions { InboundPath = inboundPath }),
            Options.Create(new ImportWorkerOptions()),
            NullLogger<ImportWorker>.Instance
        );
    }

    private static string Envelope(string ciId, string storageLocation) =>
        $$"""
            {
              "schemaVersion": "1",
              "definition": "{{DefinitionName}}",
              "records": [ { "ciId": "{{ciId}}", "storageLocation": "{{storageLocation}}" } ]
            }
            """;

    // Writes a data file + matching manifest (correct checksum unless overridden) into inboundDir.
    private static void DropFile(string inboundDir, string fileName, string content, string? checksumOverride = null)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var checksum = checksumOverride ?? Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        File.WriteAllBytes(Path.Combine(inboundDir, fileName), bytes);
        File.WriteAllText(
            Path.Combine(inboundDir, ExportSchema.BuildManifestFileName(fileName)),
            JsonSerializer.Serialize(new { Sha256Checksum = checksum })
        );
    }

    [Fact]
    public async Task PollOnceAsync_ValidFileAndManifest_StagesPendingReviewRunAndMovesFilesToProcessed()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        await using var local = await LocalDb.NewAsync();
        var db = local.Db;
        await SeedDefinitionAsync(db);

        var inboundDir = Directory.CreateTempSubdirectory("import-worker-test-");
        try
        {
            DropFile(inboundDir.FullName, "vendor-drop-1.json", Envelope(FixtureCiId, "New Location"));

            var worker = NewWorker(db, inboundDir.FullName);
            await worker.PollOnceAsync(CancellationToken.None);

            var run = Assert.Single(db.ImportRuns);
            Assert.Equal(ImportRunStatus.PendingReview, run.Status);
            Assert.Equal("watcher", run.TriggeredBy);
            Assert.Equal("vendor-drop-1.json", run.SourceFileName);
            Assert.Equal(1, run.RecordCount);
            Assert.Equal(1, run.MatchedCount);
            Assert.Equal(1, run.ChangedCount);
            Assert.Equal(0, run.UnchangedCount);
            Assert.Equal(0, run.RejectedCount);
            Assert.Equal(0, run.InvalidCount);
            Assert.NotNull(run.DefinitionSnapshotJson);
            Assert.Contains("systemconfiguration", run.DefinitionSnapshotJson);
            Assert.NotNull(run.PlanJson);
            Assert.Null(run.FinishedAt);

            Assert.False(File.Exists(Path.Combine(inboundDir.FullName, "vendor-drop-1.json")));
            Assert.False(File.Exists(Path.Combine(inboundDir.FullName, "vendor-drop-1.manifest.json")));
            Assert.True(File.Exists(Path.Combine(inboundDir.FullName, "processed", "vendor-drop-1.json")));
            Assert.True(File.Exists(Path.Combine(inboundDir.FullName, "processed", "vendor-drop-1.manifest.json")));

            var auditEntry = Assert.Single(db.AuditLog.Where(a => a.Action == "import_run_staged"));
            Assert.Equal("watcher", auditEntry.Username);
        }
        finally
        {
            inboundDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PollOnceAsync_SameFileContentDroppedTwice_DoesNotCreateSecondRun()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        await using var local = await LocalDb.NewAsync();
        var db = local.Db;
        await SeedDefinitionAsync(db);

        var inboundDir = Directory.CreateTempSubdirectory("import-worker-test-");
        try
        {
            var content = Envelope(FixtureCiId, "New Location");
            DropFile(inboundDir.FullName, "vendor-drop-1.json", content);

            var worker = NewWorker(db, inboundDir.FullName);
            await worker.PollOnceAsync(CancellationToken.None);
            Assert.Single(db.ImportRuns);

            // Same bytes, different filename — a mistaken or re-triggered re-drop of the identical vendor
            // file, exactly the scenario Open Decision #13's (ImportDefinitionId, Sha256Checksum) uniqueness
            // constraint targets.
            DropFile(inboundDir.FullName, "vendor-drop-1-retry.json", content);
            await worker.PollOnceAsync(CancellationToken.None);

            Assert.Single(db.ImportRuns);
            var auditEntry = Assert.Single(db.AuditLog.Where(a => a.Action == "import_duplicate_detected"));
            Assert.Contains("already-staged duplicate", auditEntry.Detail);

            // The duplicate file itself is still handled — moved aside, not left to be rescanned forever.
            Assert.False(File.Exists(Path.Combine(inboundDir.FullName, "vendor-drop-1-retry.json")));
            Assert.True(File.Exists(Path.Combine(inboundDir.FullName, "processed", "vendor-drop-1-retry.json")));
        }
        finally
        {
            inboundDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PollOnceAsync_ChecksumMismatch_QuarantinesFileWithoutStagingARun()
    {
        await using var local = await LocalDb.NewAsync();
        var db = local.Db;
        await SeedDefinitionAsync(db);

        var inboundDir = Directory.CreateTempSubdirectory("import-worker-test-");
        try
        {
            DropFile(
                inboundDir.FullName,
                "vendor-drop-1.json",
                Envelope(FixtureCiId, "New Location"),
                checksumOverride: new string('0', 64)
            );

            var worker = NewWorker(db, inboundDir.FullName);
            await worker.PollOnceAsync(CancellationToken.None);

            Assert.Empty(db.ImportRuns);
            Assert.False(File.Exists(Path.Combine(inboundDir.FullName, "vendor-drop-1.json")));
            Assert.True(File.Exists(Path.Combine(inboundDir.FullName, "rejected", "vendor-drop-1.json")));

            var auditEntry = Assert.Single(db.AuditLog.Where(a => a.Action == "import_file_rejected"));
            Assert.Contains("checksum mismatch", auditEntry.Detail);
        }
        finally
        {
            inboundDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PollOnceAsync_MalformedJson_QuarantinesFileWithoutCrashing()
    {
        await using var local = await LocalDb.NewAsync();
        var db = local.Db;
        await SeedDefinitionAsync(db);

        var inboundDir = Directory.CreateTempSubdirectory("import-worker-test-");
        try
        {
            DropFile(inboundDir.FullName, "vendor-drop-1.json", "{ this is not valid json");

            var worker = NewWorker(db, inboundDir.FullName);
            await worker.PollOnceAsync(CancellationToken.None);

            Assert.Empty(db.ImportRuns);
            Assert.True(File.Exists(Path.Combine(inboundDir.FullName, "rejected", "vendor-drop-1.json")));

            var auditEntry = Assert.Single(db.AuditLog.Where(a => a.Action == "import_file_rejected"));
            Assert.Contains("not valid JSON", auditEntry.Detail);
        }
        finally
        {
            inboundDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PollOnceAsync_NoAccompanyingManifest_QuarantinesFile()
    {
        await using var local = await LocalDb.NewAsync();
        var db = local.Db;
        await SeedDefinitionAsync(db);

        var inboundDir = Directory.CreateTempSubdirectory("import-worker-test-");
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(inboundDir.FullName, "vendor-drop-1.json"),
                Envelope(FixtureCiId, "New Location")
            );

            var worker = NewWorker(db, inboundDir.FullName);
            await worker.PollOnceAsync(CancellationToken.None);

            Assert.Empty(db.ImportRuns);
            Assert.True(File.Exists(Path.Combine(inboundDir.FullName, "rejected", "vendor-drop-1.json")));

            var auditEntry = Assert.Single(db.AuditLog.Where(a => a.Action == "import_file_rejected"));
            Assert.Contains("no accompanying manifest", auditEntry.Detail);
        }
        finally
        {
            inboundDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PollOnceAsync_NoEnabledDefinitionMatchesEnvelope_QuarantinesFile()
    {
        await using var local = await LocalDb.NewAsync();
        var db = local.Db;
        await SeedDefinitionAsync(db, isEnabled: false);

        var inboundDir = Directory.CreateTempSubdirectory("import-worker-test-");
        try
        {
            DropFile(inboundDir.FullName, "vendor-drop-1.json", Envelope(FixtureCiId, "New Location"));

            var worker = NewWorker(db, inboundDir.FullName);
            await worker.PollOnceAsync(CancellationToken.None);

            Assert.Empty(db.ImportRuns);
            Assert.True(File.Exists(Path.Combine(inboundDir.FullName, "rejected", "vendor-drop-1.json")));

            var auditEntry = Assert.Single(db.AuditLog.Where(a => a.Action == "import_file_rejected"));
            Assert.Contains("no enabled ImportDefinition", auditEntry.Detail);
        }
        finally
        {
            inboundDir.Delete(recursive: true);
        }
    }
}
