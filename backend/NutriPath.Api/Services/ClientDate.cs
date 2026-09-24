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
}
