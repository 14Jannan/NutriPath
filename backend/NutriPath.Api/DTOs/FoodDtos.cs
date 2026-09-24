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
    string SourceName);