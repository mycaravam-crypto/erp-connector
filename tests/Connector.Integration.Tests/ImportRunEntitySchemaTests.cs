using Connector.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Connector.Integration.Tests;

/// <summary>
/// Coverage for Slice 1b's <see cref="ImportRunEntity"/>/<see cref="ImportDefinitionEntity"/> schema
/// amendments (import-definitions.md §4/§6, Open Decisions #10-13) against an in-memory SQLite
/// <see cref="ExportLogDbContext"/> — no live ERP/testdb connection needed, since this only exercises
/// the local metadata store's own schema.
/// </summary>
public sealed class ImportRunEntitySchemaTests : SqliteDbContextTestBase
{
    private async Task<int> SeedDefinitionAsync()
    {
        var definition = new ImportDefinitionEntity
        {
            Name = "Test Definition",
            RootTable = "masterdata",
            RootMatchColumn = "id",
            RootNode = "{}",
            AllowedWritableColumns = "[]",
            CreatedBy = "tester",
            CreatedAt = "2026-09-05T00:00:00Z",
        };
        Db.ImportDefinitions.Add(definition);
        await Db.SaveChangesAsync();
        return definition.Id;
    }

    [Fact]
    public async Task ImportRunEntity_RoundTrips_WithAllSixCountsAndSnapshotAndPlan()
    {
        var definitionId = await SeedDefinitionAsync();

        var run = new ImportRunEntity
        {
            ImportDefinitionId = definitionId,
            ConfigVersion = 1,
            DefinitionSnapshotJson = """{"RootTable":"masterdata"}""",
            SourceFileName = "vendor-drop-1.json",
            Sha256Checksum = new string('a', 64),
            StartedAt = "2026-09-05T00:00:00Z",
            TriggeredBy = "watcher",
            RecordCount = 6,
            MatchedCount = 4,
            ChangedCount = 2,
            UnchangedCount = 2,
            RejectedCount = 1,
            ConflictCount = 1,
            InvalidCount = 0,
            PlanJson = """{"operations":[]}""",
        };
        Db.ImportRuns.Add(run);
        await Db.SaveChangesAsync();

        Db.ChangeTracker.Clear();
        var reloaded = await Db.ImportRuns.SingleAsync(r => r.Id == run.Id);

        Assert.Equal("""{"RootTable":"masterdata"}""", reloaded.DefinitionSnapshotJson);
        Assert.Equal("""{"operations":[]}""", reloaded.PlanJson);
        Assert.Equal(6, reloaded.RecordCount);
        Assert.Equal(4, reloaded.MatchedCount);
        Assert.Equal(2, reloaded.ChangedCount);
        Assert.Equal(2, reloaded.UnchangedCount);
        Assert.Equal(1, reloaded.RejectedCount);
        Assert.Equal(1, reloaded.ConflictCount);
        Assert.Equal(0, reloaded.InvalidCount);
    }

    [Fact]
    public async Task ImportRunEntity_DuplicateChecksumForSameDefinition_RejectedByUniqueConstraint()
    {
        var definitionId = await SeedDefinitionAsync();
        var checksum = new string('b', 64);

        Db.ImportRuns.Add(
            new ImportRunEntity
            {
                ImportDefinitionId = definitionId,
                SourceFileName = "vendor-drop-1.json",
                Sha256Checksum = checksum,
                StartedAt = "2026-09-05T00:00:00Z",
                TriggeredBy = "watcher",
            }
        );
        await Db.SaveChangesAsync();

        Db.ImportRuns.Add(
            new ImportRunEntity
            {
                ImportDefinitionId = definitionId,
                SourceFileName = "vendor-drop-1-retry.json",
                Sha256Checksum = checksum,
                StartedAt = "2026-09-05T00:05:00Z",
                TriggeredBy = "watcher",
            }
        );

        await Assert.ThrowsAsync<DbUpdateException>(() => Db.SaveChangesAsync());
    }

    [Fact]
    public async Task ImportRunEntity_SameChecksumForDifferentDefinitions_Allowed()
    {
        var definitionId1 = await SeedDefinitionAsync();
        var definitionId2 = await SeedDefinitionAsync();
        var checksum = new string('c', 64);

        Db.ImportRuns.Add(
            new ImportRunEntity
            {
                ImportDefinitionId = definitionId1,
                SourceFileName = "vendor-drop-1.json",
                Sha256Checksum = checksum,
                StartedAt = "2026-09-05T00:00:00Z",
                TriggeredBy = "watcher",
            }
        );
        Db.ImportRuns.Add(
            new ImportRunEntity
            {
                ImportDefinitionId = definitionId2,
                SourceFileName = "vendor-drop-1.json",
                Sha256Checksum = checksum,
                StartedAt = "2026-09-05T00:00:00Z",
                TriggeredBy = "watcher",
            }
        );

        await Db.SaveChangesAsync();

        Assert.Equal(2, await Db.ImportRuns.CountAsync());
    }
}
