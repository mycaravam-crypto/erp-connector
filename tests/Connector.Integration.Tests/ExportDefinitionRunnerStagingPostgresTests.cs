using System.Security.Cryptography;
using System.Text.Json;
using Connector.Core.DynamicExport;
using Connector.Core.Schema;
using Connector.Infrastructure;
using Connector.Infrastructure.DataSources;
using Connector.Infrastructure.DataSources.PostgreSql;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Connector.Integration.Tests;

/// <summary>
/// <see cref="ExportDefinitionRunner.ExecuteAsync"/> with and without a <see cref="FileSystemExportSink"/>: a
/// scheduled run writes its file and manifest to the staging folder, a manual run writes nothing. No-ops when the
/// local <c>testdb</c> isn't running (see <see cref="ErpTestFixture"/>).
/// </summary>
public sealed class ExportDefinitionRunnerStagingPostgresTests : SqliteDbContextTestBase
{
    private static readonly DataSourceProviderResolver Resolver = new([new PostgreSqlDataSourceProvider()]);

    private async Task<ExportDefinitionEntity> SeedAsync()
    {
        await Db.SetSettingAsync(SettingsKeys.ErpConnection, ErpTestFixture.Config);
        var root = new ExportNode(
            "root",
            ExportNodeKind.Root,
            null,
            null,
            null,
            null,
            null,
            null,
            [new ExportNode("id", ExportNodeKind.ScalarField, "id", null, null, null, null, null, [], true)],
            true
        );
        var def = new ExportDefinitionEntity
        {
            Name = "Staged Export",
            RootTable = "systemconfiguration",
            RootNode = ExportNodeJson.Serialize(root),
            OutputFormat = "json",
            IsEnabled = true,
            ConfigVersion = 1,
            CreatedBy = "test",
            CreatedAt = "2026-01-01T00:00:00Z",
        };
        Db.ExportDefinitions.Add(def);
        await Db.SaveChangesAsync();
        return def;
    }

    private static FileSystemExportSink NewSink(string stagingPath) =>
        new(
            Options.Create(new ExportSinkOptions { StagingPath = stagingPath }),
            NullLogger<FileSystemExportSink>.Instance
        );

    [Fact]
    public async Task ExecuteAsync_WithSink_WritesFileAndManifestAndRecordsThemOnTheRun()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        var stagingDir = Directory.CreateTempSubdirectory("export-def-staging-");
        try
        {
            var def = await SeedAsync();

            var (run, built, error) = await ExportDefinitionRunner.ExecuteAsync(
                def,
                Db,
                Resolver,
                triggeredBy: ExportDefinitionWorker.SchedulerTriggeredBy,
                isTestRun: false,
                limit: null,
                NewSink(stagingDir.FullName),
                CancellationToken.None
            );

            Assert.Null(error);
            Assert.NotNull(built);
            Assert.Equal(ExportDefinitionRunStatus.Success, run.Status);
            Assert.NotNull(run.DataFileName);

            var dataPath = Path.Combine(stagingDir.FullName, run.DataFileName);
            var bytes = await File.ReadAllBytesAsync(dataPath);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), run.Sha256);

            var manifestPath = Path.Combine(stagingDir.FullName, ExportSchema.BuildManifestFileName(run.DataFileName));
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
            Assert.Equal(JsonValueKind.Null, manifest.RootElement.GetProperty("SequenceNumber").ValueKind);
            Assert.Equal(run.Sha256, manifest.RootElement.GetProperty("Sha256Checksum").GetString());
            Assert.Equal(built.Value.RecordCount, manifest.RootElement.GetProperty("RecordCount").GetInt32());
            Assert.Equal(
                (await Db.GetProducerAsync()).InstanceId,
                manifest.RootElement.GetProperty("Producer").GetProperty("InstanceId").GetString()
            );
        }
        finally
        {
            stagingDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithoutSink_WritesNothing()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        var def = await SeedAsync();

        var (run, built, _) = await ExportDefinitionRunner.ExecuteAsync(
            def,
            Db,
            Resolver,
            triggeredBy: "alice",
            isTestRun: false,
            limit: null,
            sink: null,
            CancellationToken.None
        );

        Assert.NotNull(built);
        Assert.Null(run.DataFileName);
        Assert.Null(run.Sha256);
    }

    [Fact]
    public async Task ExecuteAsync_StagingWriteFails_FailsTheRun()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        var def = await SeedAsync();
        var missingDir = Path.Combine(Path.GetTempPath(), "export-def-staging-missing-" + Guid.NewGuid());

        var (run, built, error) = await ExportDefinitionRunner.ExecuteAsync(
            def,
            Db,
            Resolver,
            triggeredBy: ExportDefinitionWorker.SchedulerTriggeredBy,
            isTestRun: false,
            limit: null,
            NewSink(missingDir),
            CancellationToken.None
        );

        Assert.Null(built);
        Assert.NotNull(error);
        Assert.Equal(ExportDefinitionRunStatus.Failed, run.Status);
        Assert.Null(run.DataFileName);
    }
}
