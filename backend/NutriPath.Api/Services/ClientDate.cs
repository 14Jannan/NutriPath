namespace NutriPath.Api.Services;

/// <summary>
/// The app sends the user's LOCAL calendar date, because "today" on the
/// server (UTC) is yesterday in Sri Lanka between midnight and 05:30.
/// Only dates within a plausible window are accepted, so a client can't
/// log meals years into the future.
/// </summary>
public static class ClientDate
{
    // No timezone is more than 14 hours ahead of UTC, so the local date is
    // at most one day past the UTC date.
    private const int MaxDaysAhead = 1;
    private const int MaxDaysBack = 366;

    public static bool TryResolve(DateOnly? requested, out DateOnly date)
    {
        var utcToday = DateOnly.FromDateTime(DateTime.UtcNow);
        date = requested ?? utcToday;
        return date >= utcToday.AddDays(-MaxDaysBack) && date <= utcToday.AddDays(MaxDaysAhead);
    }

    public const string InvalidMessage = "Date must be within the last year and not in the future.";

    /// <summary>
    /// Resolves the user's local "now" for the assistant: the full local
    /// date-time (with its UTC offset) when the app sends one, otherwise
    /// just a date. A local time must be within a day of the server clock
    /// (any real timezone is), so a wrong device clock can't mislead it.
    /// </summary>
    public static bool TryResolveClock(DateOnly? localDate, DateTimeOffset? localNow, out ClientClock clock)
    {
        if (localNow is { } now)
        {
            clock = new ClientClock(DateOnly.FromDateTime(now.DateTime), now);
            return Math.Abs((now.UtcDateTime - DateTime.UtcNow).TotalHours) <= 24;
        }

        var ok = TryResolve(localDate, out var date);
        clock = new ClientClock(date, null);
        return ok;
    }
}

/// <summary>The user's local calendar day, and their local time when known.</summary>
public record ClientClock(DateOnly Today, DateTimeOffset? LocalNow);
