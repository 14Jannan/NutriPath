using Microsoft.Extensions.Logging.Abstractions;
using NutriPath.Api.DTOs;
using NutriPath.Api.Services;

namespace NutriPath.Api.Tests;

public class ProfileInsightTests
{
    private static UpdateGoalsRequest Request(int age = 23, decimal height = 172, decimal weight = 53.5m) =>
        new(age, "Male", height, weight, "Light", "Maintain", new() { "Peanuts" }, new() { "Vegetarian" });

    /// <summary>Records what was sent to the AI, and answers or fails on demand.</summary>
    private sealed class FakeGroq : IGroqClient
    {
        public string? LastUserMessage { get; private set; }
        public Exception? Failure { get; init; }

        public Task<NutriPath.Api.Models.GroqReply> AskAsync(string systemPrompt, string userMessage, IReadOnlyList<NutriPath.Api.Models.GroqMessage>? history = null)
        {
            LastUserMessage = userMessage;
            if (Failure != null) throw Failure;
            return Task.FromResult(new NutriPath.Api.Models.GroqReply("  • Eat well.  ", 100));
        }
    }

    private static ProfileInsightService Service(FakeGroq groq) =>
        new(new ProfileService(TestDb.Create()), groq, NullLogger<ProfileInsightService>.Instance);

    [Fact]
    public void Preview_CalculatesTargetsAndBodyMetrics_WithoutSaving()
    {
        using var db = TestDb.Create();

        var preview = new ProfileService(db).Preview(Request());

        Assert.Equal(18.1m, preview.Bmi);
        Assert.Equal("Underweight", preview.BmiCategory);
        Assert.Equal(55, preview.HealthyWeightMinKg);
        Assert.Equal(74, preview.HealthyWeightMaxKg);
        Assert.True(preview.TargetCalories > 1200);
        Assert.Empty(db.UserProfiles); // nothing stored
    }

    [Fact]
    public void Preview_HasNoAdultBmiCategoryUnder18()
    {
        using var db = TestDb.Create();

        Assert.Null(new ProfileService(db).Preview(Request(age: 15, height: 160, weight: 50)).BmiCategory);
    }

    [Fact]
    public async Task Insight_GivesTheAiTheCalculatedNumbersAndPreferences()
    {
        var groq = new FakeGroq();

        var result = await Service(groq).GetInsightAsync(Request());

        Assert.Equal("• Eat well.", result.Insight);
        Assert.Contains($"\"calories\":{result.Preview.TargetCalories}", groq.LastUserMessage);
        Assert.Contains("Underweight", groq.LastUserMessage);
        Assert.Contains("Peanuts", groq.LastUserMessage);
        Assert.Contains("Vegetarian", groq.LastUserMessage);
    }

    [Fact]
    public async Task Insight_WhenAiIsDown_StillReturnsTheNumbers()
    {
        var groq = new FakeGroq { Failure = new HttpRequestException("Groq unavailable") };

        var result = await Service(groq).GetInsightAsync(Request());

        Assert.Null(result.Insight);
        Assert.True(result.Preview.TargetCalories > 0);
    }

    [Fact]
    public async Task Insight_RejectsImplausibleValues_WithoutCallingTheAi()
    {
        var groq = new FakeGroq();

        await Assert.ThrowsAsync<ArgumentException>(() => Service(groq).GetInsightAsync(Request(weight: 25)));
        Assert.Null(groq.LastUserMessage);
    }
}
