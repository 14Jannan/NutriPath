namespace NutriPath.Api.Services;

/// <summary>
/// Consecutive days with at least one meal logged. Today not being logged
/// yet doesn't break the streak — the day isn't over — so it then counts
/// back from yesterday.
/// </summary>
public record LoggingStreak(int CurrentDays, int BestDays, bool LoggedToday)
{
    public static LoggingStreak Calculate(IEnumerable<DateOnly> loggedDays, DateOnly today)
    {
        var days = loggedDays.Where(d => d <= today).ToHashSet();
        var loggedToday = days.Contains(today);

        var current = 0;
        for (var day = loggedToday ? today : today.AddDays(-1); days.Contains(day); day = day.AddDays(-1))
            current++;

        var best = 0;
        var run = 0;
        DateOnly? previous = null;
        foreach (var day in days.Order())
        {
            run = previous is { } p && p.AddDays(1) == day ? run + 1 : 1;
            best = Math.Max(best, run);
            previous = day;
        }

        return new LoggingStreak(current, best, loggedToday);
    }
}
