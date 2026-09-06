using Connector.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Connector.Integration.Tests;

/// <summary>
/// Covers <see cref="ExportDefinitionWorker.ScheduledCandidates"/> against a real (in-memory SQLite)
/// <see cref="ExportLogDbContext"/> — the acceptance criteria from issue #21 that a disabled definition,
/// or one with <c>Schedule = null</c>, never runs automatically, before <see cref="CronSchedule"/> is even
/// consulted. Like <see cref="ExportDefinitionMigratorTests"/>, this needs no live Postgres/testdb fixture.
/// </summary>
public sealed class ExportDefinitionWorkerCandidateFilterTests : SqliteDbContextTestBase
{
    [Fact]
    public async Task ScheduledCandidates_ExcludesDisabledAndManualOnlyDefinitions()
    {
        Db.ExportDefinitions.AddRange(
            MakeDefinition("enabled-hourly", isEnabled: true, schedule: "0 * * * *"),
            MakeDefinition("disabled-hourly", isEnabled: false, schedule: "0 * * * *"),
            MakeDefinition("enabled-manual", isEnabled: true, schedule: null)
        );
        await Db.SaveChangesAsync();

        var candidates = await ExportDefinitionWorker
            .ScheduledCandidates(Db.ExportDefinitions)
            .Select(d => d.Name)
            .ToListAsync();

        Assert.Equal(["enabled-hourly"], candidates);
    }

    private static ExportDefinitionEntity MakeDefinition(string name, bool isEnabled, string? schedule) =>
        new()
        {
            Name = name,
            RootTable = "masterdata",
            RootNode = "{}",
            OutputFormat = "csv",
            IsEnabled = isEnabled,
            Schedule = schedule,
            ConfigVersion = 1,
            CreatedBy = "test",
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
        };
}
