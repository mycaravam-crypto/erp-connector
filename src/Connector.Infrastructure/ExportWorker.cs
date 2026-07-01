using System.Security.Cryptography;
using System.Text.Json;
using Connector.Core.Domain;
using Connector.Core.DynamicExport;
using Connector.Core.Interfaces;
using Connector.Core.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Connector.Infrastructure;

/// <summary>
/// Täglicher Hintergrunddienst: führt die Export-Pipeline einmal pro Tag zur konfigurierten Zeit aus.
/// </summary>
/// <remarks>
/// Fehler beim Export unterdrücken keine Ausnahme — der Run wird als Failed geloggt.
/// Der nächste Vollsnapshot heilt den Ausfall idempotent, daher kein Retry-Loop.
/// </remarks>
public sealed class ExportWorker(
    IServiceScopeFactory scopeFactory,
    IExportSink sink,
    IOptions<ExportWorkerOptions> options,
    IOptions<ExportSinkOptions> sinkOptions,
    ILogger<ExportWorker> logger
) : BackgroundService
{
    private readonly string _stagingPath = sinkOptions.Value.StagingPath;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ExportWorker started. Scheduled time: {Time}", options.Value.ScheduledTimeUtc);

        while (!stoppingToken.IsCancellationRequested)
        {
            var (scheduledTime, retentionDays) = await GetEffectiveOptionsAsync(stoppingToken);
            var delay = ComputeDelayUntilNextRun(scheduledTime);
            logger.LogInformation("Next export in {Delay:hh\\:mm\\:ss}", delay);

            await Task.Delay(delay, stoppingToken);

            if (!stoppingToken.IsCancellationRequested)
            {
                (_, retentionDays) = await GetEffectiveOptionsAsync(stoppingToken);
                await RunExportAsync(stoppingToken);
                await RunRetentionCleanupAsync(retentionDays, stoppingToken);
            }
        }
    }

    // Reads scheduler config from the AppSettings DB table; falls back to IOptions values if not set.
    private async Task<(TimeSpan ScheduledTime, int RetentionDays)> GetEffectiveOptionsAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ExportLogDbContext>();
            var setting = await db.AppSettings.FindAsync(["scheduler_config"], ct);
            if (setting is not null)
            {
                var data = JsonSerializer.Deserialize<SchedulerConfigData>(setting.Value);
                if (data is not null && TimeSpan.TryParse(data.ScheduledTimeUtc, System.Globalization.CultureInfo.InvariantCulture, out var ts) && ts >= TimeSpan.Zero && ts < TimeSpan.FromDays(1))
                    return (ts, Math.Max(1, data.RetentionDays));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to read scheduler config from database; using defaults.");
        }

        return (options.Value.ScheduledTimeUtc, options.Value.RetentionDays);
    }

    private async Task RunExportAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExportLogDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<AuditService>();

        var sequenceNo = await NextSequenceNumberAsync(db, ct);
        var run = new ExportRunEntity
        {
            SequenceNo = sequenceNo,
            ExtractedAt = DateTimeOffset.UtcNow.ToString("O"),
            Status = ExportRunStatus.Pending,
        };
        db.ExportRuns.Add(run);
        await db.SaveChangesAsync(ct);

        try
        {
            logger.LogInformation("Export #{Seq} started", sequenceNo);
            await audit.LogAsync("scheduler", "export_started", $"#{sequenceNo}");

            var mappingSetting = await db.AppSettings.FindAsync("export_mapping");
            if (mappingSetting is null)
            {
                logger.LogError("Export #{Seq}: no export_mapping configured — aborting", sequenceNo);
                run.Status = ExportRunStatus.Failed;
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("scheduler", "export_failed", $"#{sequenceNo}: no export_mapping");
                return;
            }
            var config = JsonSerializer.Deserialize<ExportMappingConfig>(mappingSetting.Value)!;

            var connSetting = await db.AppSettings.FindAsync("erp_connection");
            if (connSetting is null)
            {
                logger.LogError("Export #{Seq}: no erp_connection configured — aborting", sequenceNo);
                run.Status = ExportRunStatus.Failed;
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("scheduler", "export_failed", $"#{sequenceNo}: no erp_connection");
                return;
            }
            var connCfg = JsonSerializer.Deserialize<ErpConnectionConfig>(connSetting.Value)!;

            var cols = DynamicExportService.GetColumnNames(config);
            var extractedAt = DateTimeOffset.UtcNow;
            var gdprDenylist = await DynamicExportService.GetDeniedFieldsAsync(db);

            await using var conn = new NpgsqlConnection(DynamicExportService.BuildConnectionString(connCfg));
            await conn.OpenAsync(ct);
            var records = await DynamicExportService.ExecuteQueryAsync(conn, config, ct, gdprDenylist: gdprDenylist);

            if (records.Count == 0)
            {
                logger.LogError(
                    "Export #{Seq}: aborted — query returned 0 records. " +
                    "Possible causes: missing maintenance-plan predicate in mapping, ERP query error, or empty table.",
                    sequenceNo);
                run.Status = ExportRunStatus.Failed;
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("scheduler", "export_failed", $"#{sequenceNo}: 0 records");
                return;
            }

            var bytes = DynamicExportService.BuildExcelBytes(records, cols, ExportSchema.Version, extractedAt);
            var fileName = ExportSchema.BuildFileName(sequenceNo, extractedAt);
            var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            var package = new ExportPackage(
                new ExportManifest(sequenceNo, ExportSchema.Version, extractedAt, records.Count, checksum),
                bytes,
                fileName
            );

            await sink.WriteAsync(package, ct);

            run.RecordCount = package.Manifest.RecordCount;
            run.Sha256 = package.Manifest.Sha256Checksum;
            run.DataFileName = package.DataFileName;
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Export #{Seq} completed: {Count} records, SHA-256={Hash}",
                sequenceNo,
                package.Manifest.RecordCount,
                package.Manifest.Sha256Checksum
            );
            await audit.LogAsync(
                "scheduler",
                "export_completed",
                $"#{sequenceNo} records={package.Manifest.RecordCount}"
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Export #{Seq} failed", sequenceNo);
            run.Status = ExportRunStatus.Failed;
            await db.SaveChangesAsync(CancellationToken.None);
            await audit.LogAsync("scheduler", "export_failed", $"#{sequenceNo}: {ex.Message}");
        }
    }

    private static async Task<int> NextSequenceNumberAsync(ExportLogDbContext db, CancellationToken ct)
    {
        var max = await db.ExportRuns.MaxAsync(r => (int?)r.SequenceNo, ct);
        return (max ?? 0) + 1;
    }

    private async Task RunRetentionCleanupAsync(int retentionDays, CancellationToken ct)
    {
        if (retentionDays <= 0)
            return;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        logger.LogInformation(
            "Retention: purging artifacts older than {Cutoff:yyyy-MM-dd} (RetentionDays={Days})",
            cutoff,
            retentionDays
        );

        try
        {
            PurgeStagingFiles(cutoff);

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ExportLogDbContext>();
            await PurgeExportRunRecordsAsync(db, cutoff, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Retention cleanup failed");
        }
    }

    private void PurgeStagingFiles(DateTimeOffset cutoff)
    {
        if (!Directory.Exists(_stagingPath))
            return;

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(_stagingPath))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff.UtcDateTime)
                {
                    File.Delete(file);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Retention: could not delete file: {File}", file);
            }
        }

        if (deleted > 0)
            logger.LogInformation("Retention: {Count} staging files deleted", deleted);
    }

    private async Task PurgeExportRunRecordsAsync(ExportLogDbContext db, DateTimeOffset cutoff, CancellationToken ct)
    {
        var candidates = await db.ExportRuns.Where(r => r.Status != ExportRunStatus.Pending).ToListAsync(ct);

        var toDelete = candidates
            .Where(r =>
                DateTimeOffset.TryParse(
                    r.ExtractedAt,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var ts
                )
                && ts < cutoff
            )
            .ToList();

        if (toDelete.Count > 0)
        {
            db.ExportRuns.RemoveRange(toDelete);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Retention: {Count} export run records deleted", toDelete.Count);
        }
    }

    private static TimeSpan ComputeDelayUntilNextRun(TimeSpan scheduledTime)
    {
        var now = DateTimeOffset.UtcNow;
        var todayRun = new DateTimeOffset(now.Date + scheduledTime, TimeSpan.Zero);
        var next = todayRun > now ? todayRun : todayRun.AddDays(1);
        return next - now;
    }
}

public sealed class ExportWorkerOptions
{
    public TimeSpan ScheduledTimeUtc { get; set; } = new(6, 0, 0);

    public int RetentionDays { get; set; } = 30;
}

/// <summary>Scheduler configuration stored in AppSettings DB, overriding the appsettings.json defaults.</summary>
public record SchedulerConfigData(string ScheduledTimeUtc, int RetentionDays);
