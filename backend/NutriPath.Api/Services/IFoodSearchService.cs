using NutriPath.Api.DTOs;

namespace NutriPath.Api.Services;

public interface IFoodSearchService
{
    /// <summary>Searches the shared catalog plus the user's own foods.</summary>
    Task<List<FoodSearchResultDto>> SearchAsync(Guid userId, string query, int page, int pageSize);

    /// <summary>A food the user may see (shared, or their own), or null.</summary>
    Task<FoodSearchResultDto?> GetByIdAsync(Guid userId, Guid foodId);

    /// <summary>Adds a private food. Throws ArgumentException for implausible values or a duplicate name.</summary>
    Task<FoodSearchResultDto> CreateCustomAsync(Guid userId, CreateFoodRequest request);

    /// <summary>
    /// Updates one of the user's own foods. Throws KeyNotFoundException if it isn't theirs,
    /// ArgumentException for implausible values or a name they already use.
    /// Already-logged meals keep their original values (they store a snapshot).
    /// </summary>
    Task<FoodSearchResultDto> UpdateCustomAsync(Guid userId, Guid foodId, CreateFoodRequest request);

    /// <summary>
    /// Deletes one of the user's own foods. Throws KeyNotFoundException if it isn't theirs,
    /// InvalidOperationException if it's used in their meal log.
    /// </summary>
    Task DeleteCustomAsync(Guid userId, Guid foodId);
}
