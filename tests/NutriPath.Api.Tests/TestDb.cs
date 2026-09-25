using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.Models;

namespace NutriPath.Api.Tests;

/// <summary>
/// A fresh, isolated in-memory database per test, so the real services
/// can be exercised end to end without a running Postgres.
/// </summary>
public static class TestDb
{
    public static NutriPathDbContext Create()
    {
        var options = new DbContextOptionsBuilder<NutriPathDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new NutriPathDbContext(options);
    }

    public static User AddUser(NutriPathDbContext db, UserProfile? profile = null)
    {
        var user = new User { Email = $"{Guid.NewGuid():N}@example.com", EmailVerified = true };
        profile ??= new UserProfile();
        profile.UserId = user.Id;
        user.Profile = profile;
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    // Values per 100g serving.
    public static Food AddFood(NutriPathDbContext db, string name = "Test rice", decimal calories = 200,
        decimal protein = 10, decimal carbs = 30, decimal fat = 5, decimal fiber = 4, decimal sugar = 2, decimal sodium = 100)
    {
        var food = new Food
        {
            Name = name,
            ExternalId = Guid.NewGuid().ToString(),
            ServingSizeGrams = 100,
            Calories = calories,
            ProteinGrams = protein,
            CarbsGrams = carbs,
            FatGrams = fat,
            FiberGrams = fiber,
            SugarGrams = sugar,
            SodiumMilligrams = sodium,
        };
        db.Foods.Add(food);
        db.SaveChanges();
        return food;
    }
}
