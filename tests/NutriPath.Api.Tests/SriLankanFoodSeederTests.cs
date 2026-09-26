using NutriPath.Api.Services;

namespace NutriPath.Api.Tests;

public class SriLankanFoodSeederTests
{
    // The real seed file, as copied next to the API's build output.
    private static readonly string SeedPath = Path.Combine(
        AppContext.BaseDirectory, "Data", "Seed", "sri-lankan-foods.json");

    [Fact]
    public async Task Seeds_TheVerifiedDishes_WithTheirSourceAndServing()
    {
        var db = TestDb.Create();

        Assert.Equal(4, await SriLankanFoodSeeder.SeedAsync(db, SeedPath));

        var kottu = db.Foods.Single(f => f.Name == "Chicken Kottu Roti");
        Assert.Equal(176m, kottu.Calories);
        Assert.Equal(100m, kottu.ServingSizeGrams);
        Assert.Null(kottu.CreatedByUserId); // shared catalog, visible to everyone
        Assert.Equal("Sri Lanka Food Composition Database", db.DataSources.Single(d => d.Id == kottu.DataSourceId).Name);

        var hopper = db.Foods.Single(f => f.Name.StartsWith("Egg Hopper"));
        Assert.Equal(150m, hopper.ServingSizeGrams); // kept per hopper, not converted
        Assert.Contains("eggs", hopper.Allergens);
    }

    [Fact]
    public async Task RunningAgain_DoesNotDuplicate_AndRestoresEditedValues()
    {
        var db = TestDb.Create();
        await SriLankanFoodSeeder.SeedAsync(db, SeedPath);
        db.Foods.Single(f => f.Name == "Chicken Kottu Roti").Calories = 999;
        db.SaveChanges();

        Assert.Equal(1, await SriLankanFoodSeeder.SeedAsync(db, SeedPath));

        Assert.Equal(4, db.Foods.Count());
        Assert.Equal(2, db.DataSources.Count());
        Assert.Equal(176m, db.Foods.Single(f => f.Name == "Chicken Kottu Roti").Calories);
    }
}
