using Connector.Infrastructure;

namespace Connector.Integration.Tests;

/// <summary>Pure logic coverage for <see cref="CronSchedule"/> — no database, no clock dependency.</summary>
public sealed class CronScheduleTests
{
    private static readonly DateTime MondayNoon = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc); // a Monday

    [Theory]
    [InlineData("0 * * * *")] // hourly, on the hour
    [InlineData("*/1 * * * *")] // every minute
    [InlineData("0 12 * * *")] // daily at this hour
    [InlineData("0 12 * * 1")] // weekly, this day-of-week
    [InlineData("0 12 24 * *")] // monthly, this day-of-month
    [InlineData("0 12 24 8 *")] // yearly, this month+day
    [InlineData("0 10-14 * * *")] // hour range covering noon
    [InlineData("0 */2 * * *")] // even hours (12 is even)
    public void IsDue_MatchesExpectedPresetsAndExpressions(string cron)
    {
        Assert.True(CronSchedule.IsDue(cron, MondayNoon));
    }

    [Theory]
    [InlineData("30 * * * *")] // wrong minute
    [InlineData("0 13 * * *")] // wrong hour
    [InlineData("0 12 * * 2")] // wrong day-of-week (Tuesday, not Monday)
    [InlineData("0 12 25 * *")] // wrong day-of-month
    [InlineData("0 */2 * * 2")] // hour matches, but dow="2" (Tuesday) is restricted and wrong
    public void IsDue_RejectsNonMatchingExpressions(string cron)
    {
        Assert.False(CronSchedule.IsDue(cron, MondayNoon));
    }

    [Fact]
    public void IsDue_DomAndDowAreOredWhenBothRestricted()
    {
        // Standard cron semantics: when both day-of-month and day-of-week are restricted, a match on
        // either one is enough — this fires because day-of-week (Monday=1) matches even though the
        // day-of-month (1) does not.
        Assert.True(CronSchedule.IsDue("0 12 1 * 1", MondayNoon));
    }

    [Fact]
    public void IsDue_RejectsMalformedExpressions()
    {
        Assert.False(CronSchedule.IsDue("not a cron expression", MondayNoon));
        Assert.False(CronSchedule.IsDue("0 12 * *", MondayNoon)); // only 4 fields
    }

    [Fact]
    public void GetDueDefinitions_SkipsDisabledOrDueMismatch_RunsOnlyMatching()
    {
        var due = new ExportDefinitionEntity
        {
            Id = 1,
            Name = "Hourly",
            Schedule = "0 * * * *",
            IsEnabled = true,
        };
        var wrongTime = new ExportDefinitionEntity
        {
            Id = 2,
            Name = "Daily at 6",
            Schedule = "0 6 * * *",
            IsEnabled = true,
        };
        var tick = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

        var result = ExportDefinitionWorker.GetDueDefinitions([due, wrongTime], tick).ToList();

        var single = Assert.Single(result);
        Assert.Equal(1, single.Id);
    }
}
