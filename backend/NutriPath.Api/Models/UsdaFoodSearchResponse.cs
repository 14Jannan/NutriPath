using System.Text.Json.Serialization;

namespace NutriPath.Api.Models;

public class UsdaFoodSearchResponse
{
    [JsonPropertyName("foods")]
    public List<UsdaFoodItem> Foods { get; set; } = new();
}

public class UsdaFoodItem
{
    [JsonPropertyName("fdcId")]
    public int FdcId { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("foodNutrients")]
    public List<UsdaFoodNutrient> FoodNutrients { get; set; } = new();
}

public class UsdaFoodNutrient
{
    [JsonPropertyName("nutrientId")]
    public int NutrientId { get; set; }

    [JsonPropertyName("value")]
    public decimal Value { get; set; }
}