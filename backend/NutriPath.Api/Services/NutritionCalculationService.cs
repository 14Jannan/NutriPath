using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;

namespace NutriPath.Api.Services;

public class NutritionCalculationService : INutritionCalculationService
{
    private readonly NutriPathDbContext _db;

    public NutritionCalculationService(NutriPathDbContext db) => _db = db;

    public async Task<DailyTotals> GetDailyTotalsAsync(Guid userId, DateOnly date)
    {
        var items = await _db.MealItems
            .Where(i => i.Meal!.UserId == userId && i.Meal.Date == date)
            .ToListAsync();

        // Recomputed on every call rather than cached in a DailySummary
        // table (as Phase 0's ERD sketched). At this data volume, summing
        // a handful of rows is fast enough that caching would be premature
        // optimization — worth adding only if this query gets measurably slow.
        // Sums the snapshots, not the live Food values, so a re-sync that
        // corrects a food never rewrites a past day's totals.
        return new DailyTotals(
            items.Sum(i => i.CaloriesSnapshot),
            items.Sum(i => i.ProteinGramsSnapshot),
            items.Sum(i => i.CarbsGramsSnapshot),
            items.Sum(i => i.FatGramsSnapshot),
            items.Sum(i => i.FiberGramsSnapshot),
            items.Sum(i => i.SugarGramsSnapshot),
            items.Sum(i => i.SodiumMilligramsSnapshot));
    }
}
