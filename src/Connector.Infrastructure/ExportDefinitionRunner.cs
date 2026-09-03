using System.Text.Json;
using Connector.Core.DynamicExport;
using Connector.Core.Schema;
using Npgsql;

namespace Connector.Infrastructure;

/// <summary>
/// The one execution path behind every trigger of an <see cref="ExportDefinitionEntity"/> — manual
/// <c>run</c>/<c>test</c> (<c>ExportDefinitionEndpoints</c>) and scheduled runs
/// (<see cref="ExportDefinitionWorker"/>) all call <see cref="ExecuteAsync"/> rather than duplicating it,
/// per export-definitions-2.0.md §6: "every run — scheduled or manual — writes exactly one
/// ExportDefinitionRunEntity row", with identical Failed/error-message semantics regardless of trigger.
/// </summary>
public static class ExportDefinitionRunner
{
    public static async Task<(
        ExportDefinitionRunEntity Run,
        DynamicExportService.ExportBuildResult? Built,
        string? Error
    )> ExecuteAsync(
        ExportDefinitionEntity def,
        ExportLogDbContext db,
        string triggeredBy,
        bool isTestRun,
        int? limit,
        CancellationToken ct
    )
    {
        var run = new ExportDefinitionRunEntity
        {
            ExportDefinitionId = def.Id,
            ConfigVersion = def.ConfigVersion,
            StartedAt = DateTimeOffset.UtcNow.ToString("O"),
            Status = ExportDefinitionRunStatus.Running,
            TriggeredBy = triggeredBy,
            IsTestRun = isTestRun,
        };
        db.ExportDefinitionRuns.Add(run);
        await db.SaveChangesAsync(ct);

        async Task<(ExportDefinitionRunEntity, DynamicExportService.ExportBuildResult?, string?)> Fail(string message)
        {
            run.Status = ExportDefinitionRunStatus.Failed;
            run.FinishedAt = DateTimeOffset.UtcNow.ToString("O");
            run.ErrorMessage = message;
            await db.SaveChangesAsync(CancellationToken.None);
            return (run, null, message);
        }

        // Everything from here on — including the two deserialize calls, which throw rather than return
        // null on malformed stored JSON — stays inside this try so a corrupt RootNode/connection config
        // still finalizes the run row as Failed instead of leaving it stuck at Running forever.
        try
        {
            var root = ExportNodeJson.Deserialize(def.RootNode);
            if (root is null)
                return await Fail("Stored export tree could not be read.");

            var connRaw = await db.GetSettingRawAsync(SettingsKeys.ErpConnection);
            if (connRaw is null)
                return await Fail("No database connection configured.");
            var connCfg = JsonSerializer.Deserialize<ErpConnectionConfig>(connRaw);
            if (connCfg is null)
                return await Fail("Stored database connection config could not be read.");

            var extractedAt = DateTimeOffset.UtcNow;
            var gdprDenylist = await DynamicExportService.GetDeniedFieldsAsync(db);

            await using var conn = new NpgsqlConnection(DynamicExportService.BuildConnectionString(connCfg));
            await conn.OpenAsync(ct);

            var built = await DynamicExportService.BuildExportNodeAsync(
                conn,
                def.RootTable,
                root,
                def.OutputFormat,
                ExportSchema.Version,
                extractedAt,
                ct,
                limit,
                gdprDenylist
            );

            if (built.RecordCount == 0)
                return await Fail("Export returned 0 records.");

            run.Status = ExportDefinitionRunStatus.Success;
            run.RecordCount = built.RecordCount;
            run.FinishedAt = DateTimeOffset.UtcNow.ToString("O");
            await db.SaveChangesAsync(ct);

            return (run, built, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await Fail(ex.Message);
        }
    }
}
