using NutriPath.Api.DTOs;

namespace NutriPath.Api.Services;

public interface INutritionService
{
    Task<DailyNutritionResponse> GetDailySummaryAsync(Guid userId, DateOnly date);
}
