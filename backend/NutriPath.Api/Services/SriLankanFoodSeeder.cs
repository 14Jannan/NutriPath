using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.Models;

namespace NutriPath.Api.Services;

/// <summary>
/// Loads the curated Sri Lankan dishes (Data/Seed/sri-lankan-foods.json)
/// into the shared catalog on startup. USDA doesn't cover these dishes, so
/// they're added by hand — only with verified values, each tagged with its
/// source. Upserts by ExternalId, so re-running never duplicates and a
/// corrected value in the file updates the catalog.
/// </summary>
public static class SriLankanFoodSeeder
{
    private record SeedFile(List<SeedSource> Sources);
    private record SeedSource(string Name, string Authority, string License, List<SeedFood> Foods);
    private record SeedFood(
        string ExternalId, string Name, decimal ServingSizeGrams, decimal Calories,
        decimal ProteinGrams, decimal CarbsGrams, decimal FatGrams,
        decimal FiberGrams = 0, decimal SugarGrams = 0, decimal SodiumMilligrams = 0, string? Allergens = null);

    /// <returns>How many foods were added or updated.</returns>
    public static async Task<int> SeedAsync(NutriPathDbContext db, string jsonPath)
    {
        var file = JsonSerializer.Deserialize<SeedFile>(
            await File.ReadAllTextAsync(jsonPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (file == null) return 0;

        var changed = 0;
        foreach (var seedSource in file.Sources)
        {
            var source = await db.DataSources.FirstOrDefaultAsync(d => d.Name == seedSource.Name);
            if (source == null)
            {
                source = new DataSource { Name = seedSource.Name, Type = DataSourceType.ManualSeed };
                db.DataSources.Add(source);
            }
            source.Authority = seedSource.Authority;
            source.License = seedSource.License;
            source.LastSyncedAtUtc = DateTime.UtcNow;

            foreach (var seed in seedSource.Foods)
            {
                var food = await db.Foods.FirstOrDefaultAsync(f => f.DataSourceId == source.Id && f.ExternalId == seed.ExternalId);
                if (food == null)
                {
                    food = new Food { DataSourceId = source.Id, ExternalId = seed.ExternalId };
                    db.Foods.Add(food);
                }
                else if (Matches(food, seed))
                {
                    continue;
                }

                food.Name = seed.Name;
                food.ServingSizeGrams = seed.ServingSizeGrams;
                food.Calories = seed.Calories;
                food.ProteinGrams = seed.ProteinGrams;
                food.CarbsGrams = seed.CarbsGrams;
                food.FatGrams = seed.FatGrams;
                food.FiberGrams = seed.FiberGrams;
                food.SugarGrams = seed.SugarGrams;
                food.SodiumMilligrams = seed.SodiumMilligrams;
                food.Allergens = seed.Allergens;
                food.LastUpdatedAtUtc = DateTime.UtcNow;
                changed++;
            }
        }

        await db.SaveChangesAsync();
        return changed;
    }

    private static bool Matches(Food f, SeedFood s) =>
        f.Name == s.Name && f.ServingSizeGrams == s.ServingSizeGrams && f.Calories == s.Calories &&
        f.ProteinGrams == s.ProteinGrams && f.CarbsGrams == s.CarbsGrams && f.FatGrams == s.FatGrams &&
        f.FiberGrams == s.FiberGrams && f.SugarGrams == s.SugarGrams && f.SodiumMilligrams == s.SodiumMilligrams &&
        f.Allergens == s.Allergens;
}
