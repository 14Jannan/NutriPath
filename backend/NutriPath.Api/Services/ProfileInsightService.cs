using System.Text.Json;
using NutriPath.Api.DTOs;

namespace NutriPath.Api.Services;

/// <summary>
/// Personalised dietary guidance for the goals screen. The backend
/// calculates every number (targets, BMI, healthy range) exactly as it
/// would when saving; the AI only explains what they mean for someone of
/// this age, size, activity and goal. It never produces a number itself.
/// </summary>
public class ProfileInsightService : IProfileInsightService
{
    private const string SystemPrompt = """
        You are NutriPath's nutrition assistant, giving general wellness guidance to a
        user in Sri Lanka who is setting up their nutrition goals. STRICT RULES:
        1) Use ONLY the numbers in the data given. Never calculate, change or invent a number.
        2) Write 3-4 short bullet points, each starting with "• ", under 110 words in total:
           - what their calorie and protein targets mean for their goal;
           - nutrients that matter most at their age and sex (for example calcium and iron
             for teenagers, protein, calcium, vitamin D and B12 from age 50);
           - 2-3 everyday food ideas, preferring common Sri Lankan foods.
        3) Never suggest a food matching their "allergies"; respect "dietaryPreferences"
           (e.g. no meat for Vegetarian, no pork for Halal).
        4) If bmiCategory is Underweight or Obese, or age is under 18, kindly suggest also
           talking to a doctor or dietitian. Never diagnose, and don't mention appearance.
        5) Be encouraging and plain-spoken. No headings, no markdown other than the bullets.
        """;

    private readonly IProfileService _profileService;
    private readonly IGroqClient _groqClient;
    private readonly ILogger<ProfileInsightService> _logger;

    public ProfileInsightService(IProfileService profileService, IGroqClient groqClient, ILogger<ProfileInsightService> logger)
    {
        _profileService = profileService;
        _groqClient = groqClient;
        _logger = logger;
    }

    public async Task<ProfileInsightResponse> GetInsightAsync(UpdateGoalsRequest request)
    {
        // Throws ArgumentException for invalid values, before any AI call.
        var preview = _profileService.Preview(request);

        var facts = JsonSerializer.Serialize(new
        {
            request.Age,
            request.Sex,
            request.HeightCm,
            request.WeightKg,
            request.ActivityLevel,
            request.Goal,
            allergies = request.Allergies ?? new List<string>(),
            dietaryPreferences = request.DietaryPreferences ?? new List<string>(),
            bmi = preview.Bmi,
            bmiCategory = preview.BmiCategory ?? "not applicable under 18",
            healthyWeightRangeKg = $"{preview.HealthyWeightMinKg}-{preview.HealthyWeightMaxKg}",
            dailyTargets = new
            {
                calories = preview.TargetCalories,
                proteinGrams = preview.TargetProteinGrams,
                carbsGrams = preview.TargetCarbsGrams,
                fatGrams = preview.TargetFatGrams,
                fiberGrams = preview.TargetFiberGrams,
            },
        });

        try
        {
            var insight = await _groqClient.AskAsync(
                SystemPrompt,
                $"USER DATA (use these exact numbers):\n{facts}\n\nExplain what this means for their diet.");
            return new ProfileInsightResponse(preview, insight.Trim());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // The numbers are still correct and useful without the AI text,
            // so an AI outage degrades the feature instead of failing it.
            _logger.LogWarning(ex, "Profile insight AI call failed");
            return new ProfileInsightResponse(preview, null);
        }
    }
}
