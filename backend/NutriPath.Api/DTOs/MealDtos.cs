namespace NutriPath.Api.DTOs;

public record LogMealItemRequest(Guid FoodId, decimal QuantityGrams, string MealType, DateOnly? Date);

public record MealItemResponse(
    Guid Id, string FoodName, decimal QuantityGrams,
    decimal Calories, decimal ProteinGrams, decimal CarbsGrams,
    decimal FatGrams, decimal FiberGrams, decimal SugarGrams, decimal SodiumMilligrams);

public record MealGroupResponse(string MealType, List<MealItemResponse> Items, decimal TotalCalories);

public record DailyMealsResponse(
    DateOnly Date,
    List<MealGroupResponse> Meals,
    decimal TotalCalories, decimal TotalProteinGrams, decimal TotalCarbsGrams,
    decimal TotalFatGrams, decimal TotalFiberGrams);