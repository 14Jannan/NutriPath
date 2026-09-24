using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.Models;

namespace NutriPath.Api.Services;

/// <summary>
/// The retrieval half of RAG. Gathers real, current, structured facts
/// about this specific user from the database — never invented, never
/// left for the AI to guess — and packages them as JSON the AI will be
/// instructed to treat as ground truth. Retrieval here is plain SQL, not
/// vector search: every fact is structured, numeric data.
/// </summary>
public class AiContextBuilder : IAiContextBuilder
{
    private readonly NutriPathDbContext _db;
    private readonly INutritionCalculationService _nutritionService;
    private readonly IWeeklyScoreService _weeklyScoreService;

    public AiContextBuilder(
        NutriPathDbContext db,
        INutritionCalculationService nutritionService,
        IWeeklyScoreService weeklyScoreService)
    {
        _db = db;
        _nutritionService = nutritionService;
        _weeklyScoreService = weeklyScoreService;
    }

    public async Task<string> BuildContextAsync(Guid userId, string userQuestion)
    {
        var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dailyTotals = await _nutritionService.GetDailyTotalsAsync(userId, today);

        // The weekly score needs a profile (for targets); without one there
        // is simply no score to report, rather than an error for the chat.
        var weeklyScore = profile != null ? await _weeklyScoreService.CalculateAsync(userId) : null;

        // Simple keyword-based retrieval: search the real Foods table for
        // words from the question, so the AI can reference actual catalog
        // items with real nutrition values instead of inventing a dish.
        var candidateFoods = await FindRelevantFoodsAsync(userQuestion);

        var context = new
        {
            userGoal = profile?.Goal.ToString() ?? "Not set",
            allergies = profile?.Allergies ?? new List<string>(),
            dietaryPreferences = profile?.DietaryPreferences ?? new List<string>(),
            dailyTargets = new
            {
                calories = profile?.TargetCalories ?? 0,
                proteinGrams = profile?.TargetProteinGrams ?? 0,
                fiberGrams = profile?.TargetFiberGrams ?? 0,
            },
            todaySoFar = new
            {
                caloriesEaten = dailyTotals.Calories,
                caloriesRemaining = (profile?.TargetCalories ?? 0) - dailyTotals.Calories,
                proteinGrams = dailyTotals.Protein,
                fiberGrams = dailyTotals.Fiber,
            },
            weeklyScore = weeklyScore == null ? null : new
            {
                overall = weeklyScore.Overall,
                components = weeklyScore.Components.Select(c => new { c.Name, c.Percent }),
            },
            relevantFoodsFromDatabase = candidateFoods.Select(f => new
            {
                f.Name,
                f.Calories,
                f.ProteinGrams,
                f.FiberGrams,
                servingSizeGrams = f.ServingSizeGrams,
            }),
        };

        return JsonSerializer.Serialize(context);
    }

    private async Task<List<Food>> FindRelevantFoodsAsync(string question)
    {
        // Naive but useful keyword extraction: strip punctuation ("rice?"
        // -> "rice") and keep words longer than 3 characters to skip
        // "the", "and", "for", etc.
        var keywords = question
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim().Trim('?', '!', '.', ',', ';', ':', '"', '\''))
            .Where(w => w.Length > 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3);

        var results = new List<Food>();
        foreach (var keyword in keywords)
        {
            var matches = await _db.Foods
                .Where(f => EF.Functions.ILike(f.Name, $"%{keyword}%"))
                .Take(3)
                .ToListAsync();
            results.AddRange(matches);
        }

        return results.DistinctBy(f => f.Id).Take(5).ToList();
    }
}
