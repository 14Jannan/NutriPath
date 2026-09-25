using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.DTOs;
using NutriPath.Api.Models;

namespace NutriPath.Api.Services;

public class FoodSearchService : IFoodSearchService
{
    public const string UserFoodsSourceName = "Added by users";

    private readonly NutriPathDbContext _db;

    public FoodSearchService(NutriPathDbContext db)
    {
        _db = db;
    }

    public async Task<List<FoodSearchResultDto>> SearchAsync(Guid userId, string query, int page, int pageSize)
    {
        // EF.Functions.ILike is Postgres-specific: case-insensitive LIKE,
        // so searching "rice" matches "Rice", "RICE", "Fried rice", etc.
        // Plain C# .Contains() would translate to a case-SENSITIVE SQL
        // LIKE on Postgres by default — a real, easy-to-miss gotcha.
        var results = await _db.Foods
            .Where(Food.VisibleTo(userId))
            .Where(f => EF.Functions.ILike(f.Name, SearchPatterns.Contains(query), SearchPatterns.EscapeCharacter))
            // The user's own foods first (they added them for a reason), then
            // names that START with the query ("Rice, white, cooked" before
            // "Crackers, rice"), then shorter, more general names.
            .OrderBy(f => f.CreatedByUserId == null ? 1 : 0)
            .ThenBy(f => EF.Functions.ILike(f.Name, SearchPatterns.StartsWith(query), SearchPatterns.EscapeCharacter) ? 0 : 1)
            .ThenBy(f => f.Name.Length)
            .ThenBy(f => f.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToDto)
            .ToListAsync();

        return results;
    }

    public async Task<FoodSearchResultDto?> GetByIdAsync(Guid userId, Guid foodId)
    {
        return await _db.Foods
            .Where(Food.VisibleTo(userId))
            .Where(f => f.Id == foodId)
            .Select(ToDto)
            .FirstOrDefaultAsync();
    }

    public async Task<FoodSearchResultDto> CreateCustomAsync(Guid userId, CreateFoodRequest request)
    {
        var name = (request.Name ?? string.Empty).Trim();
        Validate(name, request);

        var nameLower = name.ToLower();
        if (await _db.Foods.AnyAsync(f => f.CreatedByUserId == userId && f.Name.ToLower() == nameLower))
            throw new ArgumentException($"You already added a food called \"{name}\".");

        var source = await GetOrCreateUserFoodsSourceAsync();
        var food = new Food
        {
            Name = name,
            DataSourceId = source.Id,
            ExternalId = Guid.NewGuid().ToString(), // unique per source, as the index requires
            CreatedByUserId = userId,
            ServingSizeGrams = 100,
            Calories = request.Calories,
            ProteinGrams = request.ProteinGrams,
            CarbsGrams = request.CarbsGrams,
            FatGrams = request.FatGrams,
            FiberGrams = request.FiberGrams ?? 0,
            SugarGrams = request.SugarGrams ?? 0,
            SodiumMilligrams = request.SodiumMilligrams ?? 0,
        };
        _db.Foods.Add(food);
        await _db.SaveChangesAsync();

        return (await GetByIdAsync(userId, food.Id))!;
    }

    public async Task DeleteCustomAsync(Guid userId, Guid foodId)
    {
        var food = await _db.Foods.FirstOrDefaultAsync(f => f.Id == foodId && f.CreatedByUserId == userId)
            ?? throw new KeyNotFoundException("Food not found.");

        // Deleting would also delete logged meals that use it (and change
        // past days' totals), so a food that's in the log stays.
        if (await _db.MealItems.AnyAsync(i => i.FoodId == foodId))
            throw new InvalidOperationException("This food is in your meal log, so it can't be deleted. Remove it from your log first.");

        _db.Foods.Remove(food);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Plausibility checks, so user-added values are at least physically
    /// possible. They're still unverified, which is why custom foods stay
    /// private to the person who added them.
    /// </summary>
    private static void Validate(string name, CreateFoodRequest r)
    {
        if (name.Length is < 2 or > 80) throw new ArgumentException("Name must be 2 to 80 characters.");

        var values = new[] { r.Calories, r.ProteinGrams, r.CarbsGrams, r.FatGrams, r.FiberGrams ?? 0, r.SugarGrams ?? 0, r.SodiumMilligrams ?? 0 };
        if (values.Any(v => v < 0)) throw new ArgumentException("Values can't be negative.");

        // Per 100 g: nothing exceeds pure fat's 900 kcal, and the macros
        // can't weigh more than the 100 g they're part of.
        if (r.Calories > 900) throw new ArgumentException("Calories can't be more than 900 per 100 g.");
        if (r.ProteinGrams + r.CarbsGrams + r.FatGrams > 100)
            throw new ArgumentException("Protein, carbs and fat together can't be more than 100 g per 100 g.");
        if ((r.SugarGrams ?? 0) > r.CarbsGrams) throw new ArgumentException("Sugar can't be more than total carbs.");
        if ((r.FiberGrams ?? 0) > 100) throw new ArgumentException("Fibre can't be more than 100 g per 100 g.");
        if ((r.SodiumMilligrams ?? 0) > 40000) throw new ArgumentException("Sodium can't be more than 40,000 mg per 100 g.");

        // Calories come from protein and carbs (4 kcal/g) and fat (9 kcal/g).
        // A generous tolerance allows for label rounding and fibre, but
        // catches typos like 30 kcal for a food with 50 g of fat.
        var expected = 4 * (r.ProteinGrams + r.CarbsGrams) + 9 * r.FatGrams;
        var tolerance = Math.Max(40m, expected * 0.35m);
        if (Math.Abs(r.Calories - expected) > tolerance)
            throw new ArgumentException(
                $"Calories don't match the protein, carbs and fat (those add up to about {Math.Round(expected)} kcal). Please check the values.");
    }

    private async Task<DataSource> GetOrCreateUserFoodsSourceAsync()
    {
        var existing = await _db.DataSources.FirstOrDefaultAsync(d => d.Name == UserFoodsSourceName);
        if (existing != null) return existing;

        var created = new DataSource
        {
            Name = UserFoodsSourceName,
            Authority = "Entered by the user; not independently verified",
            License = "Private to the user who added it",
            Type = DataSourceType.ManualSeed,
        };
        _db.DataSources.Add(created);
        await _db.SaveChangesAsync();
        return created;
    }

    // An expression (not a method) so EF translates it to SQL: only the
    // needed columns are selected and DataSource.Name becomes a JOIN,
    // with no Include required. Only the owner ever sees a custom food,
    // so it's always labelled "Added by you".
    private static readonly Expression<Func<Food, FoodSearchResultDto>> ToDto = f => new FoodSearchResultDto(
        f.Id, f.Name, f.ServingSizeGrams, f.Calories, f.ProteinGrams,
        f.CarbsGrams, f.FatGrams, f.FiberGrams, f.SugarGrams, f.SodiumMilligrams,
        f.CreatedByUserId != null ? "Added by you" : f.DataSource != null ? f.DataSource.Name : "Unknown",
        f.CreatedByUserId != null);
}
