using NutriPath.Api.Models;
using NutriPath.Api.DTOs;
using NutriPath.Api.Services;

namespace NutriPath.Api.Tests;

public class ProfileServiceTests
{
    [Fact]
    public async Task UpdateGoals_CalculatesMifflinStJeorTargets()
    {
        using var db = TestDb.Create();
        var user = TestDb.AddUser(db);
        var service = new ProfileService(db);

        var result = await service.UpdateGoalsAsync(user.Id, new UpdateGoalsRequest(
            22, "Male", 170, 65, "Moderate", "Maintain", new() { "Peanuts", " peanuts ", "" }, null));

        // BMR = 10*65 + 6.25*170 - 5*22 + 5 = 1607.5; x1.55 (Moderate) = 2491.6
        Assert.Equal(2492, result.TargetCalories);
        Assert.Equal(104, result.TargetProteinGrams); // 65kg x 1.6
        Assert.Equal(69, result.TargetFatGrams);      // 25% of kcal / 9
        Assert.Equal(364, result.TargetCarbsGrams);   // remaining kcal / 4
        Assert.Equal(35, result.TargetFiberGrams);    // 14g per 1000 kcal
        Assert.Equal(new List<string> { "Peanuts" }, result.Allergies); // trimmed, de-duplicated, blanks dropped
    }

    [Fact]
    public async Task UpdateGoals_NeverTargetsBelow1200Kcal()
    {
        using var db = TestDb.Create();
        var user = TestDb.AddUser(db);

        var result = await new ProfileService(db).UpdateGoalsAsync(user.Id, new UpdateGoalsRequest(
            13, "Female", 100, 25, "Sedentary", "Lose", null, null));

        Assert.Equal(1200, result.TargetCalories);
    }

    [Fact]
    public async Task UpdateGoals_OtherSex_UsesMidpointOfMaleAndFemaleFormulas()
    {
        using var db = TestDb.Create();
        var user = TestDb.AddUser(db);

        var result = await new ProfileService(db).UpdateGoalsAsync(user.Id, new UpdateGoalsRequest(
            22, "Other", 170, 65, "Moderate", "Maintain", null, null));

        // BMR = 1602.5 - 78 = 1524.5; x1.55 = 2363
        Assert.Equal(2363, result.TargetCalories);
        Assert.Equal("Other", result.Sex);
    }

    [Theory]
    [InlineData(5, "Male", 170, 65, "Moderate", "Maintain")]     // age out of range
    [InlineData(22, "Male", 40, 65, "Moderate", "Maintain")]     // height out of range
    [InlineData(22, "Robot", 170, 65, "Moderate", "Maintain")]   // unknown sex
    [InlineData(22, "Male", 170, 65, "Extreme", "Maintain")]     // unknown activity level
    [InlineData(23, "Male", 172, 25, "Light", "Maintain")]       // each value in range, but BMI ~8
    [InlineData(23, "Male", 150, 200, "Light", "Maintain")]      // BMI ~89
    public async Task UpdateGoals_RejectsInvalidInput(int age, string sex, decimal height, decimal weight, string activity, string goal)
    {
        using var db = TestDb.Create();
        var user = TestDb.AddUser(db);

        await Assert.ThrowsAsync<ArgumentException>(() => new ProfileService(db).UpdateGoalsAsync(
            user.Id, new UpdateGoalsRequest(age, sex, height, weight, activity, goal, null, null)));
    }
}

public class AvatarTests
{
    [Fact]
    public async Task UpdateAvatar_SavesAFixedAvatar_AndCanBeChangedLater()
    {
        using var db = TestDb.Create();
        var user = TestDb.AddUser(db);
        var service = new ProfileService(db);

        Assert.Null((await service.GetMyProfileAsync(user.Id)).AvatarId);
        Assert.Equal("panda", (await service.UpdateAvatarAsync(user.Id, "panda")).AvatarId);
        Assert.Equal("carrot", (await service.UpdateAvatarAsync(user.Id, "carrot")).AvatarId);
        Assert.Equal("carrot", db.UserProfiles.Single().AvatarId);
    }

    [Theory]
    [InlineData("dragon")]
    [InlineData("")]
    [InlineData("PANDA")] // IDs are exact
    [InlineData("<script>alert(1)</script>")]
    public async Task UpdateAvatar_RejectsAnythingOutsideTheCatalog(string avatarId)
    {
        using var db = TestDb.Create();
        var user = TestDb.AddUser(db);

        await Assert.ThrowsAsync<ArgumentException>(() => new ProfileService(db).UpdateAvatarAsync(user.Id, avatarId));
    }

    [Fact]
    public void Catalog_HasUniqueShortIds()
    {
        Assert.Equal(AvatarCatalog.Ids.Count, AvatarCatalog.Ids.Distinct().Count());
        Assert.All(AvatarCatalog.Ids, id => Assert.InRange(id.Length, 1, 40));
    }
}

public class MealServiceTests
{
    private static readonly DateOnly Day = new(2026, 9, 24);

    [Fact]
    public async Task LogItem_ScalesAndSnapshotsNutrients_AndReusesTheSameMeal()
    {
        using var db = TestDb.Create();
        var user = TestDb.AddUser(db);
        var food = TestDb.AddFood(db, calories: 200, protein: 10);
        var service = new MealService(db);

        var item = await service.LogItemAsync(user.Id, new LogMealItemRequest(food.Id, 150, "lunch", Day));
        await service.LogItemAsync(user.Id, new LogMealItemRequest(food.Id, 50, "Lunch", Day));

        Assert.Equal(300m, item.Calories);     // 150g of a 200 kcal/100g food
        Assert.Equal(15m, item.ProteinGrams);
        Assert.Single(db.Meals);               // both items went into one Lunch meal

        // Correcting the food later must not rewrite what was already logged.
        food.Calories = 999;
        await db.SaveChangesAsync();

        var daily = await service.GetDailyAsync(user.Id, Day);
        Assert.Equal(400m, daily.TotalCalories);
        Assert.Equal("Lunch", Assert.Single(daily.Meals).MealType);
    }

    [Theory]
    [InlineData(0, "Lunch")]
    [InlineData(100, "Brunch")]
    public async Task LogItem_RejectsInvalidRequests(decimal grams, string mealType)
    {
        using var db = TestDb.Create();
        var user = TestDb.AddUser(db);
        var food = TestDb.AddFood(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new MealService(db).LogItemAsync(user.Id, new LogMealItemRequest(food.Id, grams, mealType, Day)));
    }

    [Fact]
    public async Task DeleteItem_CannotDeleteAnotherUsersItem()
    {
        using var db = TestDb.Create();
        var owner = TestDb.AddUser(db);
        var other = TestDb.AddUser(db);
        var food = TestDb.AddFood(db);
        var service = new MealService(db);
        var item = await service.LogItemAsync(owner.Id, new LogMealItemRequest(food.Id, 100, "Dinner", Day));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteItemAsync(other.Id, item.Id));
        await service.DeleteItemAsync(owner.Id, item.Id);

        Assert.Empty(db.MealItems);
    }
}

public class NutritionAndScoreServiceTests
{
    private static readonly DateOnly Today = new(2026, 9, 24);

    [Fact]
    public async Task DailySummary_ReportsEatenAndRemainingAgainstTarget()
    {
        using var db = TestDb.Create();
        var user = TestDb.AddUser(db, new() { TargetCalories = 2000, TargetProteinGrams = 100 });
        var food = TestDb.AddFood(db, calories: 250, protein: 20);
        await new MealService(db).LogItemAsync(user.Id, new LogMealItemRequest(food.Id, 200, "Breakfast", Today));

        var summary = await new NutritionService(db, new NutritionCalculationService(db)).GetDailySummaryAsync(user.Id, Today);

        Assert.Equal(500, summary.EatenCalories);
        Assert.Equal(1500, summary.RemainingCalories);
        var protein = summary.Macros.Single(m => m.Label == "Protein");
        Assert.Equal(40m, protein.Current);
        Assert.Equal(100m, protein.Target);
    }

    [Fact]
    public async Task WeeklyScore_WithNoLogs_IsZeroWithNoData()
    {
        using var db = TestDb.Create();
        var user = TestDb.AddUser(db);

        var score = await new WeeklyScoreService(db).GetCurrentWeekScoreAsync(user.Id, Today);

        Assert.Equal(0, score.Overall);
        Assert.Equal(7, score.DailyScores.Count);
        Assert.All(score.Components, c => Assert.Equal("No data", c.Status));
    }

    [Fact]
    public async Task WeeklyScore_SnacksDoNotPushConsistencyOver100()
    {
        using var db = TestDb.Create();
        var user = TestDb.AddUser(db, new() { TargetCalories = 2000, TargetProteinGrams = 90, TargetFiberGrams = 28 });
        var food = TestDb.AddFood(db, calories: 500, protein: 25, fiber: 7, sugar: 5, sodium: 300);
        var meals = new MealService(db);

        for (var i = 0; i < 7; i++)
        {
            foreach (var type in new[] { "Breakfast", "Lunch", "Dinner", "Snack" })
                await meals.LogItemAsync(user.Id, new LogMealItemRequest(food.Id, 100, type, Today.AddDays(-i)));
        }

        var score = await new WeeklyScoreService(db).GetCurrentWeekScoreAsync(user.Id, Today);

        // 4 x 500 kcal = exactly on the 2000 target, every day.
        Assert.Equal(100, score.Components.Single(c => c.Key == "consistency").Percent);
        Assert.Equal(100, score.Components.Single(c => c.Key == "calories").Percent);
        Assert.Equal(100, score.Overall);
    }
}
