using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.DTOs;
using NutriPath.Api.Models;

namespace NutriPath.Api.Services;

public class FoodSearchService : IFoodSearchService
{
    private readonly NutriPathDbContext _db;

    public FoodSearchService(NutriPathDbContext db)
    {
        _db = db;
    }

    public async Task<List<FoodSearchResultDto>> SearchAsync(string query, int page, int pageSize)
    {
        // EF.Functions.ILike is Postgres-specific: case-insensitive LIKE,
        // so searching "rice" matches "Rice", "RICE", "Fried rice", etc.
        // Plain C# .Contains() would translate to a case-SENSITIVE SQL
        // LIKE on Postgres by default — a real, easy-to-miss gotcha.
        var results = await _db.Foods
            .Where(f => EF.Functions.ILike(f.Name, SearchPatterns.Contains(query)))
            .OrderBy(f => f.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToDto)
            .ToListAsync();

        return results;
    }

    public async Task<FoodSearchResultDto?> GetByIdAsync(Guid foodId)
    {
        return await _db.Foods
            .Where(f => f.Id == foodId)
            .Select(ToDto)
            .FirstOrDefaultAsync();
    }

    // An expression (not a method) so EF translates it to SQL: only the
    // needed columns are selected and DataSource.Name becomes a JOIN,
    // with no Include required.
    private static readonly Expression<Func<Food, FoodSearchResultDto>> ToDto = f => new FoodSearchResultDto(
        f.Id, f.Name, f.ServingSizeGrams, f.Calories, f.ProteinGrams,
        f.CarbsGrams, f.FatGrams, f.FiberGrams, f.SugarGrams, f.SodiumMilligrams,
        f.DataSource != null ? f.DataSource.Name : "Unknown");
}