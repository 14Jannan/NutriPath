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

    public async Task<string> BuildContextAsync(Guid userId, string userQuestion, ClientClock clock)
    {
        var today = clock.Today;
        var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);

        // Which meals are already logged today, so "what should I eat now?"
        // can suggest the meal that's actually next.
        var mealsLoggedToday = await _db.Meals
            .Where(m => m.UserId == userId && m.Date == today && m.Items.Any())
            .Select(m => m.MealType)
            .ToListAsync();
        var dailyTotals = await _nutritionService.GetDailyTotalsAsync(userId, today);

        var weeklyScore = await _weeklyScoreService.GetCurrentWeekScoreAsync(userId, today);

        // The actual log for the last 7 days (today included), so the AI can
        // talk about what was eaten, not just the totals. Snapshots, like
        // every other total, so past days never change.
        var weekStart = today.AddDays(-6);
        var weekItems = await _db.MealItems
            .Where(i => i.Meal!.UserId == userId && i.Meal.Date >= weekStart && i.Meal.Date <= today)
            .Select(i => new
            {
                i.Meal!.Date,
                i.Meal.MealType,
                Food = i.Food!.Name,
                i.QuantityGrams,
                i.CaloriesSnapshot,
                i.ProteinGramsSnapshot,
                i.FiberGramsSnapshot,
            })
            .ToListAsync();

        // A year back is plenty for "best streak".
        var loggedDays = await _db.Meals
            .Where(m => m.UserId == userId && m.Date > today.AddDays(-366) && m.Date <= today && m.Items.Any())
            .Select(m => m.Date)
            .Distinct()
            .ToListAsync();
        var streak = LoggingStreak.Calculate(loggedDays, today);

        // Simple keyword-based retrieval: search the real Foods table for
        // words from the question, so the AI can reference actual catalog
        // items with real nutrition values instead of inventing a dish.
        var candidateFoods = await FindRelevantFoodsAsync(userId, userQuestion);

        var allergies = profile?.Allergies ?? new List<string>();
        var remainingCalories = (profile?.TargetCalories ?? 0) - dailyTotals.Calories;
        var mealCandidates = await FindMealCandidatesAsync(userId, remainingCalories, allergies);

        var context = new
        {
            // The user's own clock, so the AI knows the date, day and time
            // of day where they are (not the server's UTC).
            now = new
            {
                date = today.ToString("dddd, d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
                dayOfWeek = today.DayOfWeek.ToString(),
                localTime = clock.LocalNow?.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown",
                partOfDay = clock.LocalNow is { } local ? PartOfDay(local.Hour) : "unknown",
                utcOffset = clock.LocalNow?.ToString("zzz") ?? "unknown",
            },
            mealsLoggedToday = mealsLoggedToday.Select(t => t.ToString()).OrderBy(t => t).ToList(),
            todaysLog = weekItems
                .Where(i => i.Date == today)
                .GroupBy(i => i.MealType)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    meal = g.Key.ToString(),
                    foods = g.Select(i => new
                    {
                        name = i.Food,
                        grams = Math.Round(i.QuantityGrams),
                        calories = Math.Round(i.CaloriesSnapshot),
                        proteinGrams = Math.Round(i.ProteinGramsSnapshot, 1),
                    }),
                }),
            // Oldest first, one entry per day, including days with nothing logged.
            last7Days = Enumerable.Range(0, 7).Select(offset =>
            {
                var date = weekStart.AddDays(offset);
                var items = weekItems.Where(i => i.Date == date).ToList();
                return new
                {
                    date = date.ToString("ddd d MMM", System.Globalization.CultureInfo.InvariantCulture),
                    calories = Math.Round(items.Sum(i => i.CaloriesSnapshot)),
                    proteinGrams = Math.Round(items.Sum(i => i.ProteinGramsSnapshot), 1),
                    fiberGrams = Math.Round(items.Sum(i => i.FiberGramsSnapshot), 1),
                    mealsLogged = items.Select(i => i.MealType).Distinct().Order().Select(t => t.ToString()),
                    // The most-eaten foods that day, to make the summary concrete.
                    topFoods = items.GroupBy(i => i.Food)
                        .OrderByDescending(g => g.Sum(i => i.CaloriesSnapshot))
                        .Take(3)
                        .Select(g => g.Key),
                };
            }),
            loggingStreak = new
            {
                howToRead = "Consecutive days with at least one meal logged. If loggedToday is false, the " +
                            "streak is still alive but ends tonight unless something is logged today.",
                currentDays = streak.CurrentDays,
                bestDays = streak.BestDays,
                loggedToday = streak.LoggedToday,
            },
            userGoal = profile?.Goal.ToString() ?? "Not set",
            allergies,
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
                caloriesRemaining = remainingCalories,
                proteinGrams = dailyTotals.Protein,
                fiberGrams = dailyTotals.Fiber,
            },
            weeklyScore = new
            {
                // Without this, the model has misread "Sugar: 100" as "too
                // much sugar" when it actually means no penalty at all.
                howToRead = "All scores are 0-100 where HIGHER IS BETTER. For Sugar and Sodium, 100 means " +
                            "the average stayed within the healthy limit; lower means it went over. For the " +
                            "others, 100 means the daily target was met. Consistency is the share of breakfast, " +
                            "lunch and dinner logged over the last 7 days. An overall score of 0 with every " +
                            "component 'No data' means nothing was logged this week.",
                overall = weeklyScore.Overall,
                components = weeklyScore.Components.Select(c => new { c.Label, c.Percent, c.Status, c.Note }),
            },
            relevantFoodsFromDatabase = candidateFoods.Select(f => new
            {
                f.Name,
                f.Calories,
                f.ProteinGrams,
                f.FiberGrams,
                servingSizeGrams = f.ServingSizeGrams,
            }),
            // Chosen by plain business rules (FindMealCandidatesAsync), not
            // by the AI: same remaining budget and allergies always give
            // the same list. The AI only picks among these and phrases it.
            mealSuggestionCandidates = mealCandidates.Select(f => new
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

    private static string PartOfDay(int hour) => hour switch
    {
        < 5 => "night",
        < 11 => "morning (breakfast time)",
        < 15 => "midday (lunch time)",
        < 18 => "afternoon (snack time)",
        < 22 => "evening (dinner time)",
        _ => "night",
    };

    private async Task<List<Food>> FindMealCandidatesAsync(Guid userId, decimal remainingCalories, List<string> allergies)
    {
        if (remainingCalories <= 0) return new List<Food>();

        // A food fits if one serving is within the remaining calorie budget.
        // Ranked by protein per calorie — a simple, explainable "better for
        // you" ordering. A wider page is fetched first because the allergy
        // filter below runs in memory.
        var candidates = await _db.Foods
            .Where(Food.VisibleTo(userId)) // never another user's private foods
            .Where(f => f.Calories > 0 && f.Calories <= remainingCalories)
            .OrderByDescending(f => f.ProteinGrams / f.Calories)
            .ThenBy(f => f.Name)
            .Take(50)
            .ToListAsync();

        // Checks the structured Allergens tags where a food has them, and
        // falls back to matching the name for untagged (e.g. USDA) foods —
        // a best effort, not a guarantee.
        return candidates
            .Where(f => !allergies.Any(a => MentionsAllergen(f, a)))
            .Take(5)
            .ToList();
    }

    // Also tries the singular ("Eggs" -> "Egg"), since food names like
    // "Egg, whole, raw" wouldn't contain the plural the user picked.
    private static bool MentionsAllergen(Food food, string allergy)
    {
        var terms = new[] { allergy, allergy.EndsWith('s') ? allergy[..^1] : allergy }
            .Where(t => t.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return terms.Any(t =>
            food.Name.Contains(t, StringComparison.OrdinalIgnoreCase) ||
            (food.Allergens != null && food.Allergens.Contains(t, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<List<Food>> FindRelevantFoodsAsync(Guid userId, string question)
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
                .Where(Food.VisibleTo(userId))
                .Where(f => EF.Functions.ILike(f.Name, SearchPatterns.Contains(keyword), SearchPatterns.EscapeCharacter))
                .Take(3)
                .ToListAsync();
            results.AddRange(matches);
        }

        return results.DistinctBy(f => f.Id).Take(5).ToList();
    }
}
