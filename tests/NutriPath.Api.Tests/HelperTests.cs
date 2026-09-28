using NutriPath.Api.Services;

namespace NutriPath.Api.Tests;

public class SearchPatternsTests
{
    [Theory]
    [InlineData("rice", "%rice%")]
    [InlineData("100%", @"%100\%%")]       // % is literal, not "anything"
    [InlineData("low_fat", @"%low\_fat%")] // _ is literal, not "any one character"
    [InlineData(@"a\b", @"%a\\b%")]
    public void Contains_EscapesWildcards(string input, string expected)
    {
        Assert.Equal(expected, SearchPatterns.Contains(input));
    }
}

public class ClientDateTests
{
    private static DateOnly UtcToday => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public void TryResolve_NoDate_DefaultsToUtcToday()
    {
        Assert.True(ClientDate.TryResolve(null, out var date));
        Assert.Equal(UtcToday, date);
    }

    [Fact]
    public void TryResolve_AcceptsTomorrow_ForTimezonesAheadOfUtc()
    {
        Assert.True(ClientDate.TryResolve(UtcToday.AddDays(1), out _));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-400)]
    public void TryResolve_RejectsImplausibleDates(int offsetDays)
    {
        Assert.False(ClientDate.TryResolve(UtcToday.AddDays(offsetDays), out _));
    }
}

public class LoggingStreakTests
{
    private static readonly DateOnly Today = new(2026, 9, 26);
    private static DateOnly DaysAgo(int n) => Today.AddDays(-n);

    [Fact]
    public void CountsBackFromToday_AndFindsTheBestRun()
    {
        // A 4-day run ending today, and an older 5-day run.
        var days = new[] { 0, 1, 2, 3, 10, 11, 12, 13, 14 }.Select(DaysAgo);

        var streak = LoggingStreak.Calculate(days, Today);

        Assert.Equal(new LoggingStreak(4, 5, true), streak);
    }

    [Fact]
    public void NothingLoggedYetToday_KeepsYesterdaysStreakAlive()
    {
        var streak = LoggingStreak.Calculate(new[] { DaysAgo(1), DaysAgo(2) }, Today);

        Assert.Equal(new LoggingStreak(2, 2, false), streak);
    }

    [Fact]
    public void AMissedDay_EndsTheStreak()
    {
        Assert.Equal(0, LoggingStreak.Calculate(new[] { DaysAgo(2), DaysAgo(3) }, Today).CurrentDays);
        Assert.Equal(new LoggingStreak(0, 0, false), LoggingStreak.Calculate(Array.Empty<DateOnly>(), Today));
    }
}
