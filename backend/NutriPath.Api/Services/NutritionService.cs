using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.DTOs;

namespace NutriPath.Api.Services;

/// <summary>
/// Combines real logged meals with the user's real targets into the exact
/// shape the Today Dashboard needs. The backend is the single source of
/// truth for "remaining calories" — the frontend only displays it.
/// </summary>
public class NutritionService : INutritionService
{
    private readonly NutriPathDbContext _db;
    private readonly INutritionCalculationService _calculation;

    // Reuses NutritionCalculationService's day totals rather than
    // re-querying MealItems here, so "what did they eat today" has one
    // owner (also used by the AI context and weekly score).
    public NutritionService(NutriPathDbContext db, INutritionCalculationService calculation)
    {
        _db = db;
        _calculation = calculation;
    }

    public async Task<DailyNutritionResponse> GetDailySummaryAsync(Guid userId, DateOnly date)
    {
        var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        var totals = await _calculation.GetDailyTotalsAsync(userId, date);

        // A target of 0 means goals haven't been set yet; it's returned
        // as-is so the app can prompt for goals instead of showing a
        // made-up default target as if it were the user's own.
        var targetCalories = profile?.TargetCalories ?? 0;
        var eaten = (int)Math.Round(totals.Calories);

        return new DailyNutritionResponse(
            targetCalories,
            eaten,
            targetCalories - eaten,
            new List<MacroProgress>
            {
                new("Protein", totals.Protein, profile?.TargetProteinGrams ?? 0),
                new("Carbs", totals.Carbs, profile?.TargetCarbsGrams ?? 0),
                new("Fat", totals.Fat, profile?.TargetFatGrams ?? 0),
                new("Fibre", totals.Fiber, profile?.TargetFiberGrams ?? 0),
            });
    }
}
