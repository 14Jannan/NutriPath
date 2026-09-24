using NutriPath.Api.Services;

namespace NutriPath.Api.Tests;

// Tests the real ScoringRules the WeeklyScoreService uses — pure math,
// so no database or running API is needed.
public class ScoringRulesTests
{
    [Fact]
    public void ScoreCloseness_AtExactTarget_Returns100()
    {
        Assert.Equal(100, ScoringRules.ScoreCloseness(actual: 2000, target: 2000, tolerance: 0.15));
    }

    [Theory]
    [InlineData(2150, 50)] // 7.5% over is half the 15% tolerance
    [InlineData(1850, 50)] // under-eating is scored the same as over-eating
    [InlineData(2300, 0)]  // exactly at the tolerance edge
    [InlineData(5000, 0)]  // far past it: clamped, never negative
    public void ScoreCloseness_AwayFromTarget_ScoresDownInBothDirections(double actual, int expected)
    {
        Assert.Equal(expected, ScoringRules.ScoreCloseness(actual, target: 2000, tolerance: 0.15));
    }

    [Fact]
    public void ScoreCloseness_WithNoTarget_Returns0()
    {
        Assert.Equal(0, ScoringRules.ScoreCloseness(actual: 1500, target: 0, tolerance: 0.15));
    }

    [Theory]
    [InlineData(45, 50)]   // half the target
    [InlineData(90, 100)]  // exactly on target
    [InlineData(150, 100)] // exceeding a protein/fibre target is still 100
    public void ScoreAtLeast_ScoresProportionallyUpTo100(double actual, int expected)
    {
        Assert.Equal(expected, ScoringRules.ScoreAtLeast(actual, target: 90));
    }

    [Theory]
    [InlineData(2000, 100)] // at the limit exactly
    [InlineData(1500, 100)] // well under
    [InlineData(2500, 75)]  // 25% over -> 100 - 25
    [InlineData(4000, 0)]   // 100% over -> clamped to 0
    public void ScorePenaltyAboveLimit_VariousInputs_ScoresCorrectly(double actual, int expected)
    {
        Assert.Equal(expected, ScoringRules.ScorePenaltyAboveLimit(actual, limit: 2000));
    }

    [Theory]
    [InlineData(95, "Excellent", "good")]
    [InlineData(80, "Good", "good")]
    [InlineData(65, "Moderate", "neutral")]
    [InlineData(45, "Needs attention", "warn")]
    [InlineData(10, "Low", "warn")]
    public void StatusAndTone_MatchScoreBands(int score, string status, string tone)
    {
        Assert.Equal(status, ScoringRules.StatusFor(score));
        Assert.Equal(tone, ScoringRules.ToneFor(score));
    }
}
