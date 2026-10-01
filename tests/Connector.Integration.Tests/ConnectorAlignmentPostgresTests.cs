using Connector.Core.DynamicExport;
using Connector.Core.DynamicImport;
using Connector.Infrastructure;
using Connector.Infrastructure.DataSources;
using Connector.Infrastructure.DataSources.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Connector.Integration.Tests;

/// <summary>
/// End-to-end connector-to-connector alignment (knowledge/pipeline/import-definitions.md §9) in one process: two
/// <see cref="LocalDb"/>s play instance A and instance B (each with its own instance ID), both reading/writing
/// <c>testdb</c>. A's scheduled export run writes an ImportEnvelope + manifest to its staging folder; the files
/// are "carried" into B's inbound folder; B's <see cref="ImportWorker"/> stages them and
/// <see cref="ImportRunReleaser"/> releases them into a table created for this test (B's system). Source rows are
/// the read-only <c>SN-0004x</c> fixtures, so no other test's writes can race this one. No-ops without
/// <c>testdb</c>.
/// </summary>
public sealed class ConnectorAlignmentPostgresTests
{
    private static readonly DataSourceProviderResolver Resolver = new([new PostgreSqlDataSourceProvider()]);

    private const string ImportName = "Alignment Import";
    private const string FirstCi = "44444444-4444-4444-4444-444444444444"; // active, 2024-03-15
    private const string DecommissionedCi = "66666666-6666-6666-6666-666666666666"; // decommissioned, 2022-01-10

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
            if (serviceType == typeof(IDataSourceProviderResolver))
                return Resolver;
            return null;
        }
    }

    private static ImportWorker NewWorker(ExportLogDbContext db, AuditService audit, string inboundPath) =>
        new(
            new FixedServiceScopeFactory(new FixedServiceProvider(db, audit)),
            Options.Create(new ImportSinkOptions { InboundPath = inboundPath }),
            Options.Create(new ImportWorkerOptions()),
            NullLogger<ImportWorker>.Instance
        );

    private static FileSystemExportSink NewSink(string stagingPath) =>
        new(
            Options.Create(new ExportSinkOptions { StagingPath = stagingPath }),
            NullLogger<FileSystemExportSink>.Instance
        );

    private static ExportNode ExportScalar(string targetKey, string sourceField) =>
        new(targetKey, ExportNodeKind.ScalarField, sourceField, null, null, null, null, null, [], true);

    private static ImportNode ImportScalar(string sourceKey, string targetColumn) =>
        new(
            sourceKey,
            ImportNodeKind.ScalarField,
            targetColumn,
            null,
            null,
            null,
            OnMissingChildPolicy.Reject,
            null,
            [],
            true
        );

    // Instance A: a scheduled-style export of the SN-0004x rows, routed to B's import definition.
    private static ExportDefinitionEntity ExportDefinition()
    {
        var root = new ExportNode(
            "root",
            ExportNodeKind.Root,
            null,
            null,
            null,
            null,
            "serial LIKE 'SN-0004%'",
            null,
            [
                ExportScalar("ciId", "id"),
                ExportScalar("status", "status"),
                ExportScalar("commissionDate", "commission_date"),
            ],
            true
        );
        return new ExportDefinitionEntity
        {
            Name = "Alignment Export",
            RootTable = "systemconfiguration",
            RootNode = ExportNodeJson.Serialize(root),
            OutputFormat = "json",
            IsEnabled = true,
            ConfigVersion = 1,
            CreatedBy = "test",
            CreatedAt = "2026-01-01T00:00:00Z",
            IntegrationKey = "alignment",
            ContractVersion = 1,
            TargetImportDefinition = ImportName,
        };
    }

    // Instance B: inserts unknown rows and updates known ones in its own table.
    private static ImportDefinitionEntity ImportDefinition(string table)
    {
        var root = new ImportNode(
            "root",
            ImportNodeKind.Root,
            null,
            null,
            null,
            null,
            OnMissingChildPolicy.Reject,
            null,
            [
                ImportScalar("ciId", "id"),
                ImportScalar("status", "status"),
                ImportScalar("commissionDate", "commission_date"),
            ],
            true
        );
        return new ImportDefinitionEntity
        {
            Name = ImportName,
            RootTable = table,
            RootMatchColumn = "id",
            RootNode = ImportNodeJson.Serialize(root),
            AllowedWritableColumns = """["status", "commission_date"]""",
            UnmatchedRootPolicy = UnmatchedRootPolicy.Insert,
            IsEnabled = true,
            ConfigVersion = 1,
            CreatedBy = "test",
            CreatedAt = "2026-01-01T00:00:00Z",
            IntegrationKey = "alignment",
            ContractVersion = 1,
        };
    }

    // The human carry: copies the staged data file and its manifest into an inbound folder.
    private static void Carry(string stagingDir, string dataFileName, string inboundDir)
    {
        var manifestName = Connector.Core.Schema.ExportSchema.BuildManifestFileName(dataFileName);
        File.Copy(Path.Combine(stagingDir, dataFileName), Path.Combine(inboundDir, dataFileName));
        File.Copy(Path.Combine(stagingDir, manifestName), Path.Combine(inboundDir, manifestName));
    }

    private static async Task ExecuteAsync(NpgsqlConnection erp, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, erp);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ReadAsync(NpgsqlConnection erp, string table, string column, string id)
    {
        await using var cmd = new NpgsqlCommand($"SELECT {column}::text FROM {table} WHERE id::text = @id", erp);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteScalarAsync() as string;
    }

    private static async Task<string> ExportOnAAsync(ExportLogDbContext dbA, ExportDefinitionEntity def, string staging)
    {
        var (run, built, error) = await ExportDefinitionRunner.ExecuteAsync(
            def,
            dbA,
            Resolver,
            triggeredBy: ExportDefinitionWorker.SchedulerTriggeredBy,
            isTestRun: false,
            limit: null,
            NewSink(staging),
            CancellationToken.None
        );
        Assert.Null(error);
        Assert.Equal(3, built!.Value.RecordCount);
        return run.DataFileName!;
    }

    [Fact]
    public async Task ExportOnA_CarriedToB_InsertsThenUpdates_AndAOwnFileIsRejected()
    {
        await using var erp = await ErpTestFixture.TryOpenAsync();
        if (erp is null)
            return;

        var table = "alignment_" + Guid.NewGuid().ToString("N")[..12];
        var root = Directory.CreateTempSubdirectory("alignment-");
        var stagingA1 = root.CreateSubdirectory("staging-a-1").FullName;
        var stagingA2 = root.CreateSubdirectory("staging-a-2").FullName;
        var inboundA = root.CreateSubdirectory("inbound-a").FullName;
        var inboundB = root.CreateSubdirectory("inbound-b").FullName;

        await using var a = await LocalDb.NewAsync();
        await using var b = await LocalDb.NewAsync();
        var auditA = new AuditService(a.Db, NullLogger<AuditService>.Instance);
        var auditB = new AuditService(b.Db, NullLogger<AuditService>.Instance);

        // Four-eyes release on B: alice operates, bob approves.
        Task ReleaseOnBAsync(ImportRunEntity run) =>
            ImportRunReleaser.ReleaseAsync(b.Db, run, "alice", "bob", auditB, Resolver, CancellationToken.None);

        try
        {
            await ExecuteAsync(
                erp,
                $"CREATE TABLE {table} (id uuid PRIMARY KEY, status varchar(50), commission_date date)"
            );
            var exportDef = ExportDefinition();
            a.Db.ExportDefinitions.Add(exportDef);
            await a.Db.SaveChangesAsync();
            b.Db.ImportDefinitions.Add(ImportDefinition(table));
            await b.Db.SaveChangesAsync();
            var instanceA = (await a.Db.GetProducerAsync()).InstanceId;
            Assert.NotEqual(instanceA, (await b.Db.GetProducerAsync()).InstanceId);

            // 1. A exports, the files are carried to B, B stages them: all three rows are new to B.
            var firstFile = await ExportOnAAsync(a.Db, exportDef, stagingA1);
            Carry(stagingA1, firstFile, inboundB);
            await NewWorker(b.Db, auditB, inboundB).PollOnceAsync(CancellationToken.None);

            var first = Assert.Single(b.Db.ImportRuns);
            Assert.Equal(ImportRunStatus.PendingReview, first.Status);
            Assert.Equal(3, first.InsertCount);
            Assert.Equal(0, first.MatchedCount);
            Assert.Contains(instanceA, first.Producer);

            // 2. Four-eyes release on B writes the rows, with uuid and date columns converted.
            await ReleaseOnBAsync(first);
            Assert.Equal(ImportRunStatus.Released, first.Status);
            Assert.Equal(0, first.ConflictCount);
            Assert.Equal("decommissioned", await ReadAsync(erp, table, "status", DecommissionedCi));
            Assert.Equal("2022-01-10", await ReadAsync(erp, table, "commission_date", DecommissionedCi));

            // 3. B's copy drifts; A's next export brings it back as one update, the other rows unchanged.
            await ExecuteAsync(erp, $"UPDATE {table} SET status = 'stale' WHERE id = '{FirstCi}'");
            var secondFile = await ExportOnAAsync(a.Db, exportDef, stagingA2);
            Carry(stagingA2, secondFile, inboundB);
            await NewWorker(b.Db, auditB, inboundB).PollOnceAsync(CancellationToken.None);

            var second = Assert.Single(b.Db.ImportRuns.Where(r => r.Id != first.Id));
            Assert.Equal(0, second.InsertCount);
            Assert.Equal(1, second.ChangedCount);
            Assert.Equal(2, second.UnchangedCount);
            await ReleaseOnBAsync(second);
            Assert.Equal(ImportRunStatus.Released, second.Status);
            Assert.Equal("active", await ReadAsync(erp, table, "status", FirstCi));

            // 4. A's own export dropped into A's inbound folder is quarantined, never staged.
            Carry(stagingA1, firstFile, inboundA);
            await NewWorker(a.Db, auditA, inboundA).PollOnceAsync(CancellationToken.None);
            Assert.Empty(a.Db.ImportRuns);
            Assert.True(File.Exists(Path.Combine(inboundA, "rejected", firstFile)));
        }
        finally
        {
            await ExecuteAsync(erp, $"DROP TABLE IF EXISTS {table}");
            root.Delete(recursive: true);
        }
    }
}
