using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Services;
using Xunit.Abstractions;

namespace NutriPath.Api.Tests;

/// <summary>Runs only when NUTRIPATH_LIVE_GEMINI_KEY is set, so the standard suite never calls Google.</summary>
public sealed class LiveGeminiFactAttribute : FactAttribute
{
    public LiveGeminiFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NUTRIPATH_LIVE_GEMINI_KEY")))
            Skip = "Set NUTRIPATH_LIVE_GEMINI_KEY to run the live Gemini smoke test (synthetic data only).";
    }
}

/// <summary>
/// End-to-end smoke test through the real HTTP pipeline (auth, privacy guard, prompt, Gemini,
/// validation, encrypted storage) using a synthetic demo account and fictional profile values.
/// No real user data is sent.
/// </summary>
public class LiveGeminiSmokeTests(ITestOutputHelper output)
{
    private sealed class LiveFactory : NutriPathApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("AI:Provider", "Gemini");
            builder.UseSetting("Gemini:ApiKey", Environment.GetEnvironmentVariable("NUTRIPATH_LIVE_GEMINI_KEY"));
            var model = Environment.GetEnvironmentVariable("NUTRIPATH_LIVE_GEMINI_MODEL");
            if (!string.IsNullOrWhiteSpace(model)) builder.UseSetting("Gemini:Model", model);
            // The real context builder needs PostgreSQL; the fictional profile from the
            // LLM evaluation file stands in for it.
            builder.ConfigureServices(services => services.AddScoped<IAiContextBuilder, SyntheticContext>());
        }
    }

    private sealed class SyntheticContext : IAiContextBuilder
    {
        public async Task<string> BuildContextAsync(Guid userId, string userQuestion, ClientClock clock)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "docs", "evaluation", "multilingual-llm-cases.json");
            using var file = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            return file.RootElement.GetProperty("syntheticContext").GetRawText();
        }
    }

    [LiveGeminiFact]
    public async Task SyntheticAccountGetsGroundedAnswersInEnglishTamilAndSinhala()
    {
        await using var factory = new LiveFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();
        var demo = TestDb.AddUser(db, new UserProfile
        {
            FullName = "Demo Student", TargetCalories = 2173, TargetProteinGrams = 113, TargetFiberGrams = 30,
            DietaryPreferences = ["Vegetarian"], Allergies = ["Peanuts"],
        });
        demo.Email = "demo@nutripath.test";
        var real = TestDb.AddUser(db);
        real.Email = "student@uni.lk";
        await db.SaveChangesAsync();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.CreateAccessToken(demo).token);
        (string Language, string Question)[] cases =
        [
            ("en", "What is my daily calorie target?"),
            ("ta", "என் தினசரி கலோரி இலக்கு என்ன?"),
            ("si", "මගේ දෛනික කැලරි ඉලක්කය කුමක්ද?"),
        ];
        Guid? conversation = null;
        foreach (var (language, question) in cases)
        {
            var started = DateTime.UtcNow;
            using var response = await client.PostAsJsonAsync("/api/ai/chat", new
            {
                question, language = "auto", conversationId = conversation,
                localDateTime = "2026-11-24T19:30:00+05:30",
            });
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"{language}: HTTP {(int)response.StatusCode}");
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var answer = root.GetProperty("answer").GetString()!;
            output.WriteLine($"[{language}] {(DateTime.UtcNow - started).TotalMilliseconds:F0} ms, provider={root.GetProperty("provider").GetString()}, " +
                             $"language={root.GetProperty("language").GetString()}, insufficientEvidence={root.GetProperty("insufficientEvidence").GetBoolean()}");
            output.WriteLine(answer);
            Assert.Equal("Gemini", root.GetProperty("provider").GetString());
            Assert.Equal(language, root.GetProperty("language").GetString());
            Assert.NotEqual(GroundedNutritionAnswer.Unverified(language), answer);
            Assert.Matches(@"2,?173", answer);
            conversation = root.GetProperty("conversationId").GetGuid();
            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        // The stored conversation is encrypted and holds all three turns.
        Assert.Equal(6, db.AiMessages.Count());
        Assert.DoesNotContain(db.AiMessages.ToList(), m => m.Content.Contains("கலோரி") || m.Content.Contains("කැලරි"));

        // A real (non-synthetic) account is refused by the free-tier guard; nothing is stored.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.CreateAccessToken(real).token);
        using var refused = await client.PostAsJsonAsync("/api/ai/chat", new { question = "What is my calorie target?" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Contains("provider_not_permitted", await refused.Content.ReadAsStringAsync());
        Assert.Equal(6, db.AiMessages.Count());
    }
}
