using NutriPath.Api.Models;

namespace NutriPath.Api.DTOs;

public record LogMealItemRequest(MealType MealType, Guid FoodId, decimal Servings);

public record MealItemResponse(string FoodName, decimal Servings, decimal Calories, decimal ProteinGrams);
public record MealResponse(MealType MealType, List<MealItemResponse> Items, decimal TotalCalories);
