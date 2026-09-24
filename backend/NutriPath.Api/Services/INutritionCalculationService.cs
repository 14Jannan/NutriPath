namespace NutriPath.Api.Services;

public record DailyTotals(
    decimal Calories, decimal Protein, decimal Carbs, decimal Fat, decimal Fiber, decimal Sugar, decimal Sodium);

public interface INutritionCalculationService
{
    Task<DailyTotals> GetDailyTotalsAsync(Guid userId, DateOnly date);
}
