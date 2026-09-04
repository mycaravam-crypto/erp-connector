using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Connector.Infrastructure;

/// <summary>
/// Phase 14 Slice 4 — a sibling of <see cref="ExportWorker"/>, not a replacement: that worker still owns
/// the legacy CI-to-vendor four-eyes pipeline exactly as before. This one polls
/// <see cref="ExportDefinitionEntity"/> rows instead, running any enabled definition whose
/// <see cref="ExportDefinitionEntity.Schedule"/> cron is due this minute, via the same
/// <see cref="ExportDefinitionRunner"/> path manual run/test already use — so a scheduled run and a manual
/// "Run Now" are indistinguishable in <see cref="ExportDefinitionRunEntity"/> history except for
/// <see cref="ExportDefinitionRunEntity.TriggeredBy"/>.
/// </summary>
public sealed class ExportDefinitionWorker(IServiceScopeFactory scopeFactory, ILogger<ExportDefinitionWorker> logger)
    : BackgroundService
{
    /// <summary>Marks a scheduler-triggered run in <see cref="ExportDefinitionRunEntity.TriggeredBy"/>,
    /// distinguishing it from a username on a manual run/test.</summary>
    public const string SchedulerTriggeredBy = "scheduler";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ExportDefinitionWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var delay = TruncateToMinute(now).AddMinutes(1) - now;

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (stoppingToken.IsCancellationRequested)
                break;

            await RunDueDefinitionsAsync(stoppingToken);
        }
    }

    private async Task RunDueDefinitionsAsync(CancellationToken ct)
    {
        // Truncated to the minute so every definition checked in this tick is evaluated against the same
        // instant, regardless of how long an earlier definition's run took.
        var tickMinute = TruncateToMinute(DateTime.UtcNow);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ExportLogDbContext>();

            // Broad, SQL-translatable prefilter; CronSchedule.IsDue itself is plain C# and can't run inside
            // the EF query, so the actual cron match happens after materializing candidates.
            var candidates = await ScheduledCandidates(db.ExportDefinitions).ToListAsync(ct);

            foreach (var def in GetDueDefinitions(candidates, tickMinute))
            {
                try
                {
                    var (_, built, error) = await ExportDefinitionRunner.ExecuteAsync(
                        def,
                        db,
                        triggeredBy: SchedulerTriggeredBy,
                        isTestRun: false,
                        limit: null,
                        ct
                    );

                    if (built is null)
                        logger.LogError(
                            "Scheduled export definition #{Id} '{Name}' failed: {Error}",
                            def.Id,
                            def.Name,
                            error
                        );
                    else
                        logger.LogInformation(
                            "Scheduled export definition #{Id} '{Name}' completed: {Count} records",
                            def.Id,
                            def.Name,
                            built.Value.RecordCount
                        );
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // ExportDefinitionRunner already finalizes the run row as Failed on any error inside
                    // its own try; this only guards against a scope/DB failure surfacing before or after
                    // that call, so one broken definition never stops the rest of the tick from running.
                    logger.LogError(
                        ex,
                        "Scheduled export definition #{Id} '{Name}' threw unexpectedly",
                        def.Id,
                        def.Name
                    );
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "ExportDefinitionWorker tick failed");
        }
    }

    /// <summary>The SQL-translatable prefilter — enabled, with a schedule — applied before any per-row
    /// cron evaluation. Exposed so a test can assert a disabled or manual-only (<c>Schedule = null</c>)
    /// definition is excluded before <see cref="CronSchedule"/> ever sees it (export-definitions-2.0.md
    /// #21 acceptance criteria).</summary>
    public static IQueryable<ExportDefinitionEntity> ScheduledCandidates(IQueryable<ExportDefinitionEntity> source) =>
        source.Where(d => d.IsEnabled && d.Schedule != null);

    /// <summary>Pure selection logic, split out from <see cref="RunDueDefinitionsAsync"/> so it's testable
    /// without a database or a real clock: which of <paramref name="candidates"/> (already filtered to
    /// enabled, non-null-schedule rows) are due at <paramref name="tickMinute"/>.</summary>
    public static IEnumerable<ExportDefinitionEntity> GetDueDefinitions(
        IEnumerable<ExportDefinitionEntity> candidates,
        DateTime tickMinute
    ) => candidates.Where(d => CronSchedule.IsDue(d.Schedule!, tickMinute));

    private static DateTime TruncateToMinute(DateTime dt) =>
        new(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, DateTimeKind.Utc);
}
