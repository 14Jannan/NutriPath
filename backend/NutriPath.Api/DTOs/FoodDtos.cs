namespace NutriPath.Api.DTOs;

public record FoodSearchResultDto(
    Guid Id,
    string Name,
    decimal ServingSizeGrams,
    decimal Calories,
    decimal ProteinGrams,
    decimal CarbsGrams,
    decimal FatGrams,
    decimal FiberGrams,
    decimal SugarGrams,
    decimal SodiumMilligrams,
    string SourceName,
    // True for a food the current user added themselves.
    bool IsCustom = false);

/// <summary>
/// A food the user adds themselves, with nutrients per 100 g (as on most
/// package labels). Fibre, sugar and sodium are optional.
/// </summary>
public record CreateFoodRequest(
    string Name,
    decimal Calories,
    decimal ProteinGrams,
    decimal CarbsGrams,
    decimal FatGrams,
    decimal? FiberGrams = null,
    decimal? SugarGrams = null,
    decimal? SodiumMilligrams = null);