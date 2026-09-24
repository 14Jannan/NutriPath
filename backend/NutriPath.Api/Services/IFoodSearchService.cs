using NutriPath.Api.DTOs;

namespace NutriPath.Api.Services;

public interface IFoodSearchService
{
    Task<List<FoodSearchResultDto>> SearchAsync(string query, int page, int pageSize);
    Task<FoodSearchResultDto?> GetByIdAsync(Guid foodId);
}