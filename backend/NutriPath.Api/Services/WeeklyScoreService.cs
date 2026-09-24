using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.DTOs;
using NutriPath.Api.Models;
using static NutriPath.Api.Services.ScoringRules;

namespace NutriPath.Api.Services;

/// <summary>
/// Deterministic and explainable: every number traces back to a plain
/// formula (see ScoringRules) over real logged data. Identical logs always
/// produce identical scores — the AI explains this result, it never
/// produces it. Weights match the 25/20/20/15/10/10 split from Phase 0.
/// </summary>
public class WeeklyScoreService : IWeeklyScoreService
{
    private const decimal SugarLimitGrams = 50;   // WHO guideline: ~10% of a 2000kcal diet
    private const decimal SodiumLimitMg = 2300;   // standard daily sodium ceiling

    // Used only when the user hasn't set goals yet, so the score still
    // means something instead of every target-based component reading 0.
    private const int DefaultCalories = 2000;
    private const int DefaultProtein = 90;
    private const int DefaultFiber = 28;

    private static readonly MealType[] MainMeals = { MealType.Breakfast, MealType.Lunch, MealType.Dinner };

    private readonly NutriPathDbContext _db;

    public WeeklyScoreService(NutriPathDbContext db)
    {
        _db = db;
    }

    public async Task<WeeklyScoreResponse> GetCurrentWeekScoreAsync(Guid userId)
    {
        var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        var targetCalories = profile is { TargetCalories: > 0 } ? profile.TargetCalories : DefaultCalories;
        var targetProtein = profile is { TargetProteinGrams: > 0 } ? profile.TargetProteinGrams : DefaultProtein;
        var targetFiber = profile is { TargetFiberGrams: > 0 } ? profile.TargetFiberGrams : DefaultFiber;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var last7Days = Enumerable.Range(0, 7).Select(i => today.AddDays(-i)).Reverse().ToList();
        var startDate = last7Days.First();

        // One query for the whole week, grouped in memory by day — far
        // cheaper than 7 separate database round-trips.
        var items = await _db.MealItems
            .Include(i => i.Meal)
            .Where(i => i.Meal!.UserId == userId && i.Meal.Date >= startDate && i.Meal.Date <= today)
            .ToListAsync();

        var dailyTotals = last7Days.Select(date =>
        {
            var dayItems = items.Where(i => i.Meal!.Date == date).ToList();
            return new
            {
                Date = date,
                Calories = dayItems.Sum(i => i.CaloriesSnapshot),
                Protein = dayItems.Sum(i => i.ProteinGramsSnapshot),
                Fiber = dayItems.Sum(i => i.FiberGramsSnapshot),
                Sugar = dayItems.Sum(i => i.SugarGramsSnapshot),
                Sodium = dayItems.Sum(i => i.SodiumMilligramsSnapshot),
                // Out of Breakfast/Lunch/Dinner = 3. Snacks don't count,
                // so this can't exceed 3 and push consistency over 100.
                MainMealsLogged = dayItems
                    .Select(i => i.Meal!.MealType)
                    .Where(t => MainMeals.Contains(t))
                    .Distinct()
                    .Count(),
            };
        }).ToList();

        var daysWithData = dailyTotals.Where(d => d.Calories > 0).ToList();

        // Nothing logged all week scores 0 — an honest "no data", not a
        // misleadingly average default.
        if (daysWithData.Count == 0)
        {
            return new WeeklyScoreResponse(0, dailyTotals.Select(_ => 0).ToList(), EmptyComponents());
        }

        // Nutrient averages cover only days with data, so a skipped day
        // doesn't read as a day of eating nothing; skipped days are
        // reflected in consistency instead.
        var avgCalories = daysWithData.Average(d => (double)d.Calories);
        var avgProtein = daysWithData.Average(d => (double)d.Protein);
        var avgFiber = daysWithData.Average(d => (double)d.Fiber);
        var avgSugar = daysWithData.Average(d => (double)d.Sugar);
        var avgSodium = daysWithData.Average(d => (double)d.Sodium);
        var consistencyRatio = dailyTotals.Average(d => d.MainMealsLogged / 3.0);

        var caloriesScore = ScoreCloseness(avgCalories, targetCalories, tolerance: 0.15);
        var proteinScore = ScoreAtLeast(avgProtein, targetProtein);
        var fiberScore = ScoreAtLeast(avgFiber, targetFiber);
        var sugarScore = ScorePenaltyAboveLimit(avgSugar, (double)SugarLimitGrams);
        var sodiumScore = ScorePenaltyAboveLimit(avgSodium, (double)SodiumLimitMg);
        var consistencyScore = (int)Math.Round(consistencyRatio * 100);

        var overall = (int)Math.Round(
            caloriesScore * 0.25 + proteinScore * 0.20 + fiberScore * 0.20 +
            sugarScore * 0.15 + sodiumScore * 0.10 + consistencyScore * 0.10);

        var dailyScores = dailyTotals.Select(d =>
        {
            if (d.Calories == 0) return 0;
            var dayCal = ScoreCloseness((double)d.Calories, targetCalories, 0.15);
            var dayProtein = ScoreAtLeast((double)d.Protein, targetProtein);
            var dayFiber = ScoreAtLeast((double)d.Fiber, targetFiber);
            return (int)Math.Round(dayCal * 0.4 + dayProtein * 0.3 + dayFiber * 0.3);
        }).ToList();

        var targetNote = profile is { TargetCalories: > 0 } ? "" : " (default — set your goals for a personal target)";

        var components = new List<ScoreComponent>
        {
            Component("calories", "Calories", caloriesScore,
                $"Averaged {Math.Round(avgCalories)} kcal/day against a {targetCalories} kcal target{targetNote}"),
            Component("protein", "Protein", proteinScore,
                $"Averaged {Math.Round(avgProtein)}g/day against a {targetProtein}g target{targetNote}"),
            Component("fibre", "Fibre", fiberScore,
                $"Averaged {Math.Round(avgFiber)}g/day against a {targetFiber}g target{targetNote}"),
            Component("sugar", "Sugar", sugarScore,
                $"Averaged {Math.Round(avgSugar)}g/day, limit is {SugarLimitGrams}g"),
            Component("sodium", "Sodium", sodiumScore,
                $"Averaged {Math.Round(avgSodium)}mg/day, limit is {SodiumLimitMg}mg"),
            Component("consistency", "Consistency", consistencyScore,
                $"Logged all 3 main meals on {dailyTotals.Count(d => d.MainMealsLogged == 3)} of 7 days"),
        };

        return new WeeklyScoreResponse(overall, dailyScores, components);
    }

    private static ScoreComponent Component(string key, string label, int score, string note) =>
        new(key, label, StatusFor(score), score, note, ToneFor(score));

    private static List<ScoreComponent> EmptyComponents() =>
        new (string Key, string Label)[]
        {
            ("calories", "Calories"), ("protein", "Protein"), ("fibre", "Fibre"),
            ("sugar", "Sugar"), ("sodium", "Sodium"), ("consistency", "Consistency"),
        }
        .Select(c => new ScoreComponent(c.Key, c.Label, "No data", 0, "Log some meals this week to see this score", "neutral"))
        .ToList();
}
