using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NutriPath.Api.Models;
using NutriPath.Api.Services;

namespace NutriPath.Api.Tests;

public class UsdaFoodSyncTests
{
    /// <summary>A fake USDA API: answers per data type, and records every URL requested.</summary>
    private sealed class FakeUsda : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = new();
        public Dictionary<string, object[]> FoodsByType { get; init; } = new();
        public HashSet<string> FailingTypes { get; init; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = Uri.UnescapeDataString(request.RequestUri!.ToString());
            RequestedUrls.Add(url);
            var dataType = url.Split("dataType=")[1].Split('&')[0];

            if (FailingTypes.Contains(dataType))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));

            var body = JsonSerializer.Serialize(new { foods = FoodsByType.GetValueOrDefault(dataType, Array.Empty<object>()) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private static object Food(int fdcId, string name, params (int Id, decimal Value)[] nutrients) => new
    {
        fdcId,
        description = name,
        foodNutrients = nutrients.Select(n => new { nutrientId = n.Id, value = n.Value }).ToArray(),
    };

    private static (UsdaFoodSyncService Service, NutriPath.Api.Data.NutriPathDbContext Db) Create(FakeUsda usda)
    {
        var db = TestDb.Create();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Usda:ApiKey"] = "test" }).Build();
        return (new UsdaFoodSyncService(new HttpClient(usda), db, config), db);
    }

    [Fact]
    public async Task Sync_RequestsOnlyGenericFoodTypes_OneAtATime()
    {
        var usda = new FakeUsda();
        var (service, _) = Create(usda);

        await service.SyncAsync("rice", 10);

        Assert.Equal(3, usda.RequestedUrls.Count);
        Assert.Contains(usda.RequestedUrls, u => u.Contains("dataType=Foundation&"));
        Assert.Contains(usda.RequestedUrls, u => u.Contains("dataType=SR Legacy&"));
        Assert.Contains(usda.RequestedUrls, u => u.Contains("dataType=Survey (FNDDS)&"));
        Assert.DoesNotContain(usda.RequestedUrls, u => u.Contains("Branded"));
    }

    [Fact]
    public async Task Sync_ReadsAtwaterEnergyAndNleaSugar_WhenStandardIdsAreMissing()
    {
        var usda = new FakeUsda
        {
            FoodsByType = { ["Foundation"] = new[] { Food(1, "Rice, black, raw", (2047, 370), (1003, 8), (1063, 1.2m)) } },
        };
        var (service, db) = Create(usda);

        await service.SyncAsync("rice", 10);

        var food = Assert.Single(db.Foods);
        Assert.Equal(370m, food.Calories); // not 0
        Assert.Equal(1.2m, food.SugarGrams);
    }

    [Fact]
    public async Task Sync_SkipsFoodsWithNoEnergyValue_AndDeduplicatesAcrossTypes()
    {
        var usda = new FakeUsda
        {
            FoodsByType =
            {
                ["SR Legacy"] = new[] { Food(1, "Rice, white, cooked", (1008, 130)), Food(2, "Mystery item") },
                ["Survey (FNDDS)"] = new[] { Food(1, "Rice, white, cooked", (1008, 130)) }, // same fdcId again
            },
        };
        var (service, db) = Create(usda);

        var job = await service.SyncAsync("rice", 10);

        var food = Assert.Single(db.Foods);
        Assert.Equal("Rice, white, cooked", food.Name);
        Assert.Equal(1, job.RecordsInserted);
        Assert.Equal(1, job.RecordsFailed); // the item with no energy value
    }

    [Fact]
    public async Task Sync_KeepsOtherTypesResults_WhenOneTypeFails()
    {
        var usda = new FakeUsda
        {
            FailingTypes = { "Foundation" },
            FoodsByType = { ["SR Legacy"] = new[] { Food(5, "Coconut milk, raw", (1008, 230)) } },
        };
        var (service, db) = Create(usda);

        var job = await service.SyncAsync("coconut milk", 10);

        Assert.Equal(SyncJobStatus.Completed, job.Status);
        Assert.StartsWith("Partly failed", job.ErrorMessage);
        Assert.Single(db.Foods);
    }

    [Fact]
    public async Task Sync_FailsTheJob_OnlyWhenEveryTypeFails()
    {
        var usda = new FakeUsda { FailingTypes = { "Foundation", "SR Legacy", "Survey (FNDDS)" } };
        var (service, _) = Create(usda);

        var job = await service.SyncAsync("rice", 10);

        Assert.Equal(SyncJobStatus.Failed, job.Status);
    }
}
