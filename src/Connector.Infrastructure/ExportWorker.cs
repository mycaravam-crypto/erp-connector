using System.Text.Json;
using Connector.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    IExportFilter filter,
    IDataMinimizer minimizer,
    ISchemaMapper mapper,
    IPackager packager,
    IExportSink sink,
    IOptions<ExportWorkerOptions> options,
    IOptions<ExportSinkOptions> sinkOptions,
    ILogger<ExportWorker> logger
) : BackgroundService
{
    private readonly string _stagingPath = sinkOptions.Value.StagingPath;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ExportWorker gestartet. Geplante Zeit: {Time}", options.Value.ScheduledTimeUtc);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeDelayUntilNextRun();
            logger.LogInformation("Nächster Export in {Delay:hh\\:mm\\:ss}", delay);

            await Task.Delay(delay, stoppingToken);

            if (!stoppingToken.IsCancellationRequested)
            {
                await RunExportAsync(stoppingToken);
                await RunRetentionCleanupAsync(stoppingToken);
            }
        }
    }

    private async Task RunExportAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExportLogDbContext>();
        var erpReader = scope.ServiceProvider.GetRequiredService<IErpReader>();

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
            logger.LogInformation("Export #{Seq} gestartet", sequenceNo);

            var mappingSetting = await db.AppSettings.FindAsync("column_mappings");
            var nameMap = mappingSetting is null
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, string>>(mappingSetting.Value);

            var rawItems = await erpReader.ReadMaintainableCIsAsync(ct);
            var filtered = filter.Filter(rawItems);
            var minimized = filtered.Select(minimizer.Minimize).ToList();
            var mapped = minimized.Select(mapper.Map).ToList();
            var package = await packager.PackageAsync(mapped, sequenceNo, ct, nameMap);

            await sink.WriteAsync(package, ct);

            run.RecordCount = package.Manifest.RecordCount;
            run.Sha256 = package.Manifest.Sha256Checksum;
            run.DataFileName = package.DataFileName;
            // Status bleibt Pending — Vier-Augen-Freigabe erfolgt manuell über die UI.
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Export #{Seq} abgeschlossen: {Count} Records, SHA-256={Hash}",
                sequenceNo,
                package.Manifest.RecordCount,
                package.Manifest.Sha256Checksum
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Export #{Seq} fehlgeschlagen", sequenceNo);
            run.Status = ExportRunStatus.Failed;
            await db.SaveChangesAsync(CancellationToken.None); // Fehler-Status muss trotz Abbruch gespeichert werden.
        }
    }

    private static async Task<int> NextSequenceNumberAsync(ExportLogDbContext db, CancellationToken ct)
    {
        var max = await db.ExportRuns.MaxAsync(r => (int?)r.SequenceNo, ct);
        return (max ?? 0) + 1;
    }

    private async Task RunRetentionCleanupAsync(CancellationToken ct)
    {
        var retentionDays = options.Value.RetentionDays;
        if (retentionDays <= 0)
            return;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        logger.LogInformation(
            "Retention: Bereinige Artefakte älter als {Cutoff:yyyy-MM-dd} (RetentionDays={Days})",
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
            // Retention-Fehler dürfen den nächsten Export nicht blockieren.
            logger.LogError(ex, "Retention-Bereinigung fehlgeschlagen");
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
                logger.LogWarning(ex, "Retention: Datei konnte nicht gelöscht werden: {File}", file);
            }
        }

        if (deleted > 0)
            logger.LogInformation("Retention: {Count} Staging-Dateien gelöscht", deleted);
    }

    private async Task PurgeExportRunRecordsAsync(ExportLogDbContext db, DateTimeOffset cutoff, CancellationToken ct)
    {
        // Nur abgeschlossene Runs löschen — Pending-Runs sind noch aktiv (warten auf Freigabe).
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
            logger.LogInformation("Retention: {Count} ExportRun-Einträge gelöscht", toDelete.Count);
        }
    }

    private TimeSpan ComputeDelayUntilNextRun()
    {
        var now = DateTimeOffset.UtcNow;
        var todayRun = new DateTimeOffset(now.Date + options.Value.ScheduledTimeUtc, TimeSpan.Zero);
        var next = todayRun > now ? todayRun : todayRun.AddDays(1);
        return next - now;
    }
}

public sealed class ExportWorkerOptions
{
    /// <summary>UTC-Zeit, zu der der tägliche Export läuft. Konfigurierbar via appsettings.json.</summary>
    public TimeSpan ScheduledTimeUtc { get; set; } = new(6, 0, 0); // Default: 06:00 UTC

    /// <summary>
    /// Anzahl der Tage, nach denen Staging-Dateien und abgeschlossene ExportRun-Einträge gelöscht werden.
    /// 0 = Retention-Bereinigung deaktiviert.
    /// </summary>
    public int RetentionDays { get; set; } = 30;
}
