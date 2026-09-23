using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.Models;

namespace NutriPath.Api.Services;

public class UsdaFoodSyncService : IUsdaFoodSyncService
{
    // USDA's own numeric IDs for the nutrients we care about — this
    // mapping is the one piece of knowledge specific to USDA's API.
    private const int NutrientId_Energy = 1008;
    private const int NutrientId_Protein = 1003;
    private const int NutrientId_Carbs = 1005;
    private const int NutrientId_Fat = 1004;
    private const int NutrientId_Fiber = 1079;
    private const int NutrientId_Sugar = 2000;
    private const int NutrientId_Sodium = 1093;

    private readonly HttpClient _httpClient;
    private readonly NutriPathDbContext _db;
    private readonly IConfiguration _config;

    public UsdaFoodSyncService(HttpClient httpClient, NutriPathDbContext db, IConfiguration config)
    {
        _httpClient = httpClient;
        _db = db;
        _config = config;
    }

    public async Task<SyncJob> SyncAsync(string query, int pageSize)
    {
        var dataSource = await GetOrCreateUsdaDataSourceAsync();

        var job = new SyncJob { DataSourceId = dataSource.Id };
        _db.SyncJobs.Add(job);
        await _db.SaveChangesAsync();

        try
        {
            var apiKey = _config["Usda:ApiKey"];
            var url = $"https://api.nal.usda.gov/fdc/v1/foods/search" +
                      $"?query={Uri.EscapeDataString(query)}&pageSize={pageSize}&api_key={apiKey}";

            var response = await _httpClient.GetFromJsonAsync<UsdaFoodSearchResponse>(url);

            job.RecordsFetched = response?.Foods.Count ?? 0;

            foreach (var item in response?.Foods ?? new())
            {
                try
                {
                    var wasInserted = await UpsertFoodAsync(item, dataSource.Id);
                    if (wasInserted) job.RecordsInserted++;
                    else job.RecordsUpdated++;
                }
                catch
                {
                    job.RecordsFailed++;
                }
            }

            dataSource.LastSyncedAtUtc = DateTime.UtcNow;
            job.Status = SyncJobStatus.Completed;
        }
        catch (Exception ex)
        {
            job.Status = SyncJobStatus.Failed;
            job.ErrorMessage = ex.Message;
        }
        finally
        {
            job.CompletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return job;
    }

    private async Task<DataSource> GetOrCreateUsdaDataSourceAsync()
    {
        var existing = await _db.DataSources.FirstOrDefaultAsync(d => d.Name == "USDA FoodData Central");
        if (existing != null) return existing;

        var created = new DataSource
        {
            Name = "USDA FoodData Central",
            Authority = "U.S. Department of Agriculture",
            License = "Public Domain (CC0)",
            Type = DataSourceType.ExternalApi,
        };
        _db.DataSources.Add(created);
        await _db.SaveChangesAsync();
        return created;
    }

    private async Task<bool> UpsertFoodAsync(UsdaFoodItem item, Guid dataSourceId)
    {
        var externalId = item.FdcId.ToString();

        var existing = await _db.Foods.FirstOrDefaultAsync(
            f => f.DataSourceId == dataSourceId && f.ExternalId == externalId);

        decimal NutrientValue(int nutrientId) =>
            item.FoodNutrients.FirstOrDefault(n => n.NutrientId == nutrientId)?.Value ?? 0;

        if (existing == null)
        {
            _db.Foods.Add(new Food
            {
                Name = item.Description,
                DataSourceId = dataSourceId,
                ExternalId = externalId,
                Calories = NutrientValue(NutrientId_Energy),
                ProteinGrams = NutrientValue(NutrientId_Protein),
                CarbsGrams = NutrientValue(NutrientId_Carbs),
                FatGrams = NutrientValue(NutrientId_Fat),
                FiberGrams = NutrientValue(NutrientId_Fiber),
                SugarGrams = NutrientValue(NutrientId_Sugar),
                SodiumMilligrams = NutrientValue(NutrientId_Sodium),
            });
            await _db.SaveChangesAsync();
            return true; // inserted
        }
        else
        {
            existing.Name = item.Description;
            existing.Calories = NutrientValue(NutrientId_Energy);
            existing.ProteinGrams = NutrientValue(NutrientId_Protein);
            existing.CarbsGrams = NutrientValue(NutrientId_Carbs);
            existing.FatGrams = NutrientValue(NutrientId_Fat);
            existing.FiberGrams = NutrientValue(NutrientId_Fiber);
            existing.SugarGrams = NutrientValue(NutrientId_Sugar);
            existing.SodiumMilligrams = NutrientValue(NutrientId_Sodium);
            existing.LastUpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return false; // updated
        }
    }
}