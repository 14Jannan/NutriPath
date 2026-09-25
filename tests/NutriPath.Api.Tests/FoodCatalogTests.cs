using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NutriPath.Api.DTOs;
using NutriPath.Api.Models;
using NutriPath.Api.Services;

namespace NutriPath.Api.Tests;

public class CustomFoodTests
{
    // Kottu roti per 100 g: 4*(7+25) + 9*8 = 200 kcal expected.
    private static CreateFoodRequest Kottu(string name = "Chicken kottu") =>
        new(name, Calories: 205, ProteinGrams: 7, CarbsGrams: 25, FatGrams: 8, FiberGrams: 2, SugarGrams: 2, SodiumMilligrams: 450);

    [Fact]
    public async Task Create_AddsAPrivateFood_Per100g()
    {
        using var db = TestDb.Create();
        var user = TestDb.AddUser(db);

        var food = await new FoodSearchService(db).CreateCustomAsync(user.Id, Kottu());

        Assert.Equal("Chicken kottu", food.Name);
        Assert.Equal(100m, food.ServingSizeGrams);
        Assert.True(food.IsCustom);
        Assert.Equal("Added by you", food.SourceName);
        Assert.Equal(user.Id, db.Foods.Single().CreatedByUserId);
    }

    [Theory]
    [InlineData(-5, 7, 25, 8, "negative")]
    [InlineData(950, 7, 25, 8, "900")]
    [InlineData(500, 40, 40, 40, "100 g")]
    [InlineData(30, 7, 25, 8, "don't match")] // typo: macros add up to ~200 kcal
    public async Task Create_RejectsImpossibleValues(decimal kcal, decimal protein, decimal carbs, decimal fat, string expected)
    {
        using var db = TestDb.Create();
        var user = TestDb.AddUser(db);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            new FoodSearchService(db).CreateCustomAsync(user.Id, new CreateFoodRequest("Test food", kcal, protein, carbs, fat)));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task Create_RejectsADuplicateNameForTheSameUser()
    {
        using var db = TestDb.Create();
        var user = TestDb.AddUser(db);
        var service = new FoodSearchService(db);
        await service.CreateCustomAsync(user.Id, Kottu());

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateCustomAsync(user.Id, Kottu("CHICKEN KOTTU")));
    }

    [Fact]
    public async Task CustomFoods_ArePrivate_ToTheUserWhoAddedThem()
    {
        using var db = TestDb.Create();
        var owner = TestDb.AddUser(db);
        var other = TestDb.AddUser(db);
        var service = new FoodSearchService(db);
        var food = await service.CreateCustomAsync(owner.Id, Kottu());

        Assert.NotNull(await service.GetByIdAsync(owner.Id, food.Id));
        Assert.Null(await service.GetByIdAsync(other.Id, food.Id));

        // ...and can't be logged by someone else, even with its id.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new MealService(db).LogItemAsync(other.Id, new LogMealItemRequest(food.Id, 200, "Dinner", new DateOnly(2026, 9, 25))));
    }

    [Fact]
    public async Task Delete_OnlyOwnFoods_AndNotOnesInTheLog()
    {
        using var db = TestDb.Create();
        var owner = TestDb.AddUser(db);
        var other = TestDb.AddUser(db);
        var service = new FoodSearchService(db);
        var logged = await service.CreateCustomAsync(owner.Id, Kottu());
        var unused = await service.CreateCustomAsync(owner.Id, Kottu("Egg hopper"));
        await new MealService(db).LogItemAsync(owner.Id, new LogMealItemRequest(logged.Id, 250, "Dinner", new DateOnly(2026, 9, 25)));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeleteCustomAsync(other.Id, unused.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteCustomAsync(owner.Id, logged.Id));
        await service.DeleteCustomAsync(owner.Id, unused.Id);

        Assert.Single(db.Foods);
    }
}

public class FoodLookupTests
{
    private static readonly Guid User = Guid.NewGuid();

    private static FoodSearchResultDto Result(string name) =>
        new(Guid.NewGuid(), name, 100, 100, 1, 1, 1, 0, 0, 0, "USDA FoodData Central");

    /// <summary>Local search that "gains" foods once the fake USDA import has run.</summary>
    private sealed class FakeSearch : IFoodSearchService
    {
        public List<FoodSearchResultDto> Local { get; set; } = new();
        public int Calls { get; private set; }

        public Task<List<FoodSearchResultDto>> SearchAsync(Guid userId, string query, int page, int pageSize)
        {
            Calls++;
            return Task.FromResult(Local.ToList());
        }

        public Task<FoodSearchResultDto?> GetByIdAsync(Guid userId, Guid foodId) => throw new NotImplementedException();
        public Task<FoodSearchResultDto> CreateCustomAsync(Guid userId, CreateFoodRequest request) => throw new NotImplementedException();
        public Task DeleteCustomAsync(Guid userId, Guid foodId) => throw new NotImplementedException();
    }

    private sealed class FakeUsda : IUsdaFoodSyncService
    {
        public List<string> Queries { get; } = new();
        public Action? OnSync { get; init; }
        public int Inserted { get; init; } = 3;
        public bool Throw { get; init; }

        public Task<SyncJob> SyncAsync(string query, int pageSize)
        {
            Queries.Add(query);
            if (Throw) throw new HttpRequestException("USDA down");
            OnSync?.Invoke();
            return Task.FromResult(new SyncJob { RecordsInserted = Inserted });
        }
    }

    private static FoodLookupService Create(FakeSearch search, FakeUsda usda, IMemoryCache? cache = null)
    {
        var services = new ServiceCollection().AddSingleton<IUsdaFoodSyncService>(usda).BuildServiceProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Usda:ApiKey"] = "test" }).Build();
        return new FoodLookupService(search, services.GetRequiredService<IServiceScopeFactory>(),
            cache ?? new MemoryCache(new MemoryCacheOptions()), config, NullLogger<FoodLookupService>.Instance);
    }

    [Fact]
    public async Task FewLocalResults_ImportsFromUsda_AndSearchesAgain()
    {
        var search = new FakeSearch();
        var usda = new FakeUsda { OnSync = () => search.Local = new() { Result("Pizza, cheese"), Result("Pizza, pepperoni") } };

        var results = await Create(search, usda).SearchAsync(User, "Pizza", 1, 20);

        Assert.Equal(new[] { "pizza" }, usda.Queries);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task PlentyOfLocalResults_DoesNotCallUsda()
    {
        var search = new FakeSearch { Local = Enumerable.Range(0, 5).Select(i => Result($"Rice {i}")).ToList() };
        var usda = new FakeUsda();

        await Create(search, usda).SearchAsync(User, "rice", 1, 20);

        Assert.Empty(usda.Queries);
    }

    [Fact]
    public async Task ATermIsLookedUpOnlyOnce_EvenIfUsdaHasNothing()
    {
        var search = new FakeSearch();
        var usda = new FakeUsda { Inserted = 0 }; // e.g. "kottu": not in USDA
        var cache = new MemoryCache(new MemoryCacheOptions());

        await Create(search, usda, cache).SearchAsync(User, "kottu", 1, 20);
        await Create(search, usda, cache).SearchAsync(User, "Kottu ", 1, 20); // another request, same term

        Assert.Single(usda.Queries);
    }

    [Theory]
    [InlineData("pi", 1)] // too short to be worth a lookup
    [InlineData("pizza", 2)] // a later page: the user already has results
    public async Task ShortTermsAndLaterPages_DoNotCallUsda(string query, int page)
    {
        var usda = new FakeUsda();

        await Create(new FakeSearch(), usda).SearchAsync(User, query, page, 20);

        Assert.Empty(usda.Queries);
    }

    [Fact]
    public async Task UsdaFailure_StillReturnsLocalResults()
    {
        var search = new FakeSearch { Local = new() { Result("Pizza, frozen") } };

        var results = await Create(search, new FakeUsda { Throw = true }).SearchAsync(User, "pizza", 1, 20);

        Assert.Single(results);
    }
}
