using NutriPath.Api.DTOs;

namespace NutriPath.Api.Services;

public interface IMealService
{
    Task<MealItemResponse> LogItemAsync(Guid userId, LogMealItemRequest request);
    Task<DailyMealsResponse> GetDailyAsync(Guid userId, DateOnly date);
    Task DeleteItemAsync(Guid userId, Guid mealItemId);
}