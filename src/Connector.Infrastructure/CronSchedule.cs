namespace Connector.Infrastructure;

/// <summary>
/// Minimal 5-field cron matcher (minute hour day-of-month month day-of-week, UTC) for
/// <see cref="ExportDefinitionEntity.Schedule"/>. Not a general cron library: no seconds field, no
/// `L`/`W`/`#` extensions — only what the UI's Manual/Hourly/Daily/Weekly presets plus a free-text
/// advanced field (export-definitions-2.0.md §6) can produce. Deliberately new rather than a NuGet
/// dependency — the matching logic is a few dozen lines and pulling in a scheduling library for it
/// would violate the "minimal code" directive (§0).
/// </summary>
public static class CronSchedule
{
    /// <summary>True when <paramref name="utcNow"/>, truncated to the minute, matches every field of
    /// <paramref name="cronExpression"/>. Day-of-month and day-of-week are OR'd together when both are
    /// restricted (i.e. neither is "*"), matching standard cron semantics.</summary>
    public static bool IsDue(string cronExpression, DateTime utcNow)
    {
        var fields = cronExpression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
            return false;

        if (!MatchesField(fields[0], utcNow.Minute, 0, 59))
            return false;
        if (!MatchesField(fields[1], utcNow.Hour, 0, 23))
            return false;
        if (!MatchesField(fields[3], utcNow.Month, 1, 12))
            return false;

        var domRestricted = fields[2] != "*";
        var dowRestricted = fields[4] != "*";
        if (!domRestricted && !dowRestricted)
            return true;

        var domMatch = domRestricted && MatchesField(fields[2], utcNow.Day, 1, 31);
        var dowMatch = dowRestricted && MatchesField(fields[4], (int)utcNow.DayOfWeek, 0, 6);
        return domMatch || dowMatch;
    }

    // One cron field: "*", "*/step", a comma-separated list of values and/or ranges ("a-b"), each
    // optionally with its own "/step".
    private static bool MatchesField(string field, int value, int min, int max)
    {
        foreach (var part in field.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var stepSplit = part.Split('/', 2);
            var step = stepSplit.Length == 2 && int.TryParse(stepSplit[1], out var s) && s > 0 ? s : 1;

            if (!TryParseRange(stepSplit[0], min, max, out var rangeStart, out var rangeEnd))
                continue; // Unparseable segment — never matches, so a malformed field is inert, not a crash.

            if (value < rangeStart || value > rangeEnd)
                continue;
            if ((value - rangeStart) % step == 0)
                return true;
        }
        return false;
    }

    // "*" → [min, max]; "a-b" → [a, b]; "a" → [a, a]. False for anything else, leaving the caller's
    // range variables untouched (they're only read after a true return).
    private static bool TryParseRange(string range, int min, int max, out int rangeStart, out int rangeEnd)
    {
        if (range == "*")
        {
            rangeStart = min;
            rangeEnd = max;
            return true;
        }

        if (range.Split('-', 2) is [var lo, var hi])
        {
            var loOk = int.TryParse(lo, out rangeStart);
            var hiOk = int.TryParse(hi, out rangeEnd);
            return loOk && hiOk;
        }

        if (int.TryParse(range, out var single))
        {
            rangeStart = single;
            rangeEnd = single;
            return true;
        }

        rangeStart = 0;
        rangeEnd = 0;
        return false;
    }
}
