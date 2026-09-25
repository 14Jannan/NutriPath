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
