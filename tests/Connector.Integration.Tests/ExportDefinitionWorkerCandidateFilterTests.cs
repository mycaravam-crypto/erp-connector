using Connector.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Connector.Integration.Tests;

/// <summary>
/// Covers <see cref="ExportDefinitionWorker.ScheduledCandidates"/> against a real (in-memory SQLite)
/// <see cref="ExportLogDbContext"/> — the acceptance criteria from issue #21 that a disabled definition,
/// or one with <c>Schedule = null</c>, never runs automatically, before <see cref="CronSchedule"/> is even
/// consulted. Like <see cref="ExportDefinitionMigratorTests"/>, this needs no live Postgres/testdb fixture.
/// </summary>
public sealed class ExportDefinitionWorkerCandidateFilterTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ExportLogDbContext _db;

    public ExportDefinitionWorkerCandidateFilterTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ExportLogDbContext>().UseSqlite(_connection).Options;
        _db = new ExportLogDbContext(options);
        _db.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ScheduledCandidates_ExcludesDisabledAndManualOnlyDefinitions()
    {
        _db.ExportDefinitions.AddRange(
            MakeDefinition("enabled-hourly", isEnabled: true, schedule: "0 * * * *"),
            MakeDefinition("disabled-hourly", isEnabled: false, schedule: "0 * * * *"),
            MakeDefinition("enabled-manual", isEnabled: true, schedule: null)
        );
        await _db.SaveChangesAsync();

        var candidates = await ExportDefinitionWorker
            .ScheduledCandidates(_db.ExportDefinitions)
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
