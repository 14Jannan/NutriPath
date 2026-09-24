using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;

namespace NutriPath.Api.Services;

/// <summary>
/// Deterministic and explainable: every number traces back to a plain
/// formula over real logged data. Identical logs always produce identical
/// scores — the AI explains this result, it never produces it.
/// </summary>
public class WeeklyScoreService : IWeeklyScoreService
{
    private readonly NutriPathDbContext _db;
    private readonly INutritionCalculationService _nutritionService;

    public WeeklyScoreService(NutriPathDbContext db, INutritionCalculationService nutritionService)
    {
        _db = db;
        _nutritionService = nutritionService;
    }

    public async Task<WeeklyScoreResult> CalculateAsync(Guid userId)
    {
        var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId)
            ?? throw new InvalidOperationException("Profile not found.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var last7Days = Enumerable.Range(0, 7).Select(offset => today.AddDays(-offset)).ToList();

        var dailyTotalsList = new List<(DateOnly Date, DailyTotals Totals)>();
        foreach (var date in last7Days)
        {
            dailyTotalsList.Add((date, await _nutritionService.GetDailyTotalsAsync(userId, date)));
        }

        int daysWithAnyLog = dailyTotalsList.Count(d => d.Totals.Calories > 0);

        // Same weighted-average formula from docs/phase-0-architecture.md.
        int calorieScore = ScoreCloseness(
            dailyTotalsList.Average(d => (double)d.Totals.Calories), profile.TargetCalories);
        int proteinScore = ScoreCloseness(
            dailyTotalsList.Average(d => (double)d.Totals.Protein), profile.TargetProteinGrams, allowOver: true);
        int fiberScore = ScoreCloseness(
            dailyTotalsList.Average(d => (double)d.Totals.Fiber), profile.TargetFiberGrams, allowOver: true);
        int sugarScore = ScorePenalty(
            dailyTotalsList.Average(d => (double)d.Totals.Sugar), limit: 50); // WHO-guideline-style limit, grams/day
        int sodiumScore = ScorePenalty(
            dailyTotalsList.Average(d => (double)d.Totals.Sodium), limit: 2300); // mg/day
        int consistencyScore = (int)Math.Round((daysWithAnyLog / 7.0) * 100);

        var components = new List<ComponentScore>
        {
            new("Calories", calorieScore),
            new("Protein", proteinScore),
            new("Fiber", fiberScore),
            new("Sugar", sugarScore),
            new("Sodium", sodiumScore),
            new("Consistency", consistencyScore),
        };

        // Weights match the 25/20/20/15/10/10 split from Phase 0's design.
        var weights = new Dictionary<string, double>
        {
            ["Calories"] = 0.25, ["Protein"] = 0.20, ["Fiber"] = 0.20,
            ["Sugar"] = 0.15, ["Sodium"] = 0.10, ["Consistency"] = 0.10,
        };

        double overall = components.Sum(c => c.Percent * weights[c.Name]);

        return new WeeklyScoreResult((int)Math.Round(overall), components);
    }

    private static int ScoreCloseness(double actual, double target, bool allowOver = false)
    {
        if (target <= 0) return 0;
        double ratio = actual / target;
        if (allowOver && ratio >= 1) return 100; // meeting-or-exceeding a protein/fiber target is fully good
        double diff = Math.Abs(1 - ratio);
        return (int)Math.Max(0, Math.Round((1 - diff) * 100));
    }

    private static int ScorePenalty(double actual, double limit)
    {
        if (actual <= limit) return 100;
        double overBy = (actual - limit) / limit;
        return (int)Math.Max(0, Math.Round((1 - overBy) * 100));
    }
}
