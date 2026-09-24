using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.DTOs;
using NutriPath.Api.Models;

namespace NutriPath.Api.Services;

public class MealService : IMealService
{
    private readonly NutriPathDbContext _db;

    public MealService(NutriPathDbContext db)
    {
        _db = db;
    }

    public async Task<MealItemResponse> LogItemAsync(Guid userId, LogMealItemRequest request)
    {
        if (request.QuantityGrams <= 0)
        {
            throw new InvalidOperationException("Quantity must be greater than zero.");
        }

        var food = await _db.Foods.FirstOrDefaultAsync(f => f.Id == request.FoodId)
            ?? throw new InvalidOperationException("Food not found.");

        if (!Enum.TryParse<MealType>(request.MealType, ignoreCase: true, out var mealType))
        {
            throw new InvalidOperationException("Invalid meal type. Use Breakfast, Lunch, Dinner, or Snack.");
        }

        var date = request.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // Find today's meal of this type, or create it — this is the same
        // "find-or-create" pattern we used for the USDA DataSource back
        // in Phase 4, applied to a different problem.
        var meal = await _db.Meals
            .FirstOrDefaultAsync(m => m.UserId == userId && m.Date == date && m.MealType == mealType);

        if (meal == null)
        {
            meal = new Meal { UserId = userId, Date = date, MealType = mealType };
            _db.Meals.Add(meal);
        }

        // Scale the food's per-serving nutrients to the actual quantity
        // logged (see NutrientScaling).
        if (food.ServingSizeGrams <= 0)
        {
            throw new InvalidOperationException("This food has no valid serving size and can't be logged.");
        }

        decimal Scale(decimal perServing) =>
            NutrientScaling.Scale(perServing, food.ServingSizeGrams, request.QuantityGrams);

        var mealItem = new MealItem
        {
            MealId = meal.Id,
            FoodId = food.Id,
            QuantityGrams = request.QuantityGrams,
            CaloriesSnapshot = Scale(food.Calories),
            ProteinGramsSnapshot = Scale(food.ProteinGrams),
            CarbsGramsSnapshot = Scale(food.CarbsGrams),
            FatGramsSnapshot = Scale(food.FatGrams),
            FiberGramsSnapshot = Scale(food.FiberGrams),
            SugarGramsSnapshot = Scale(food.SugarGrams),
            SodiumMilligramsSnapshot = Scale(food.SodiumMilligrams),
        };

        _db.MealItems.Add(mealItem);
        await _db.SaveChangesAsync();

        return new MealItemResponse(
            mealItem.Id, food.Name, mealItem.QuantityGrams,
            mealItem.CaloriesSnapshot, mealItem.ProteinGramsSnapshot, mealItem.CarbsGramsSnapshot,
            mealItem.FatGramsSnapshot, mealItem.FiberGramsSnapshot, mealItem.SugarGramsSnapshot,
            mealItem.SodiumMilligramsSnapshot);
    }

    public async Task<DailyMealsResponse> GetDailyAsync(Guid userId, DateOnly date)
    {
        var meals = await _db.Meals
            .Include(m => m.Items).ThenInclude(i => i.Food)
            .Where(m => m.UserId == userId && m.Date == date)
            .ToListAsync();

        var mealGroups = meals.Select(m => new MealGroupResponse(
            m.MealType.ToString(),
            m.Items.Select(i => new MealItemResponse(
                i.Id, i.Food?.Name ?? "Unknown", i.QuantityGrams,
                i.CaloriesSnapshot, i.ProteinGramsSnapshot, i.CarbsGramsSnapshot,
                i.FatGramsSnapshot, i.FiberGramsSnapshot, i.SugarGramsSnapshot, i.SodiumMilligramsSnapshot))
                .ToList(),
            m.Items.Sum(i => i.CaloriesSnapshot)))
            .ToList();

        var allItems = meals.SelectMany(m => m.Items).ToList();

        return new DailyMealsResponse(
            date,
            mealGroups,
            allItems.Sum(i => i.CaloriesSnapshot),
            allItems.Sum(i => i.ProteinGramsSnapshot),
            allItems.Sum(i => i.CarbsGramsSnapshot),
            allItems.Sum(i => i.FatGramsSnapshot),
            allItems.Sum(i => i.FiberGramsSnapshot));
    }

    public async Task DeleteItemAsync(Guid userId, Guid mealItemId)
    {
        var item = await _db.MealItems
            .Include(i => i.Meal)
            .FirstOrDefaultAsync(i => i.Id == mealItemId && i.Meal!.UserId == userId)
            ?? throw new InvalidOperationException("Meal item not found.");

        _db.MealItems.Remove(item);
        await _db.SaveChangesAsync();
    }
}