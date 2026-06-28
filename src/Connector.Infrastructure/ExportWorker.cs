using Connector.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
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
    IErpReader erpReader,
    IExportFilter filter,
    IDataMinimizer minimizer,
    ISchemaMapper mapper,
    IPackager packager,
    IExportSink sink,
    ExportLogDbContext db,
    IOptions<ExportWorkerOptions> options,
    ILogger<ExportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ExportWorker gestartet. Geplante Zeit: {Time}", options.Value.ScheduledTimeUtc);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeDelayUntilNextRun();
            logger.LogInformation("Nächster Export in {Delay:hh\\:mm\\:ss}", delay);

            await Task.Delay(delay, stoppingToken);

            if (!stoppingToken.IsCancellationRequested)
                await RunExportAsync(stoppingToken);
        }
    }

    private async Task RunExportAsync(CancellationToken ct)
    {
        var sequenceNo = await NextSequenceNumberAsync(ct);
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

            var rawItems = await erpReader.ReadMaintainableCIsAsync(ct);
            var filtered = filter.Filter(rawItems);
            var minimized = filtered.Select(minimizer.Minimize).ToList();
            var mapped = minimized.Select(mapper.Map).ToList();
            var package = await packager.PackageAsync(mapped, sequenceNo, ct);

            await sink.WriteAsync(package, ct);

            run.RecordCount = package.Manifest.RecordCount;
            run.Sha256 = package.Manifest.Sha256Checksum;
            run.DataFileName = package.DataFileName;
            // Status bleibt Pending — Vier-Augen-Freigabe erfolgt manuell über die UI.
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Export #{Seq} abgeschlossen: {Count} Records, SHA-256={Hash}",
                sequenceNo, package.Manifest.RecordCount, package.Manifest.Sha256Checksum);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Export #{Seq} fehlgeschlagen", sequenceNo);
            run.Status = ExportRunStatus.Failed;
            await db.SaveChangesAsync(CancellationToken.None); // Fehler-Status muss trotz Abbruch gespeichert werden.
        }
    }

    private async Task<int> NextSequenceNumberAsync(CancellationToken ct)
    {
        var max = await db.ExportRuns.MaxAsync(r => (int?)r.SequenceNo, ct);
        return (max ?? 0) + 1;
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
}
