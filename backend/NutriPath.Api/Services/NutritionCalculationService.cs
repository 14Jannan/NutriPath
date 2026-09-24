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
            .Include(i => i.Food)
            .Where(i => i.Meal!.UserId == userId && i.Meal.Date == date)
            .ToListAsync();

        // Recomputed on every call rather than cached in a DailySummary
        // table (as Phase 0's ERD sketched). At this data volume, summing
        // a handful of rows is fast enough that caching would be premature
        // optimization — worth adding only if this query gets measurably slow.
        return new DailyTotals(
            items.Sum(i => i.Food!.Calories * i.Servings),
            items.Sum(i => i.Food!.ProteinGrams * i.Servings),
            items.Sum(i => i.Food!.CarbsGrams * i.Servings),
            items.Sum(i => i.Food!.FatGrams * i.Servings),
            items.Sum(i => i.Food!.FiberGrams * i.Servings),
            items.Sum(i => i.Food!.SugarGrams * i.Servings),
            items.Sum(i => i.Food!.SodiumMilligrams * i.Servings));
    }
}
