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
    // Newer "Foundation" foods often report energy only via the Atwater
    // factors (2047 general, 2048 specific) instead of 1008, and total
    // sugars as 1063 instead of 2000. Without these fallbacks they'd be
    // stored as 0 kcal.
    private const int NutrientId_EnergyAtwaterGeneral = 2047;
    private const int NutrientId_EnergyAtwaterSpecific = 2048;
    private const int NutrientId_SugarNlea = 1063;

    // Generic, per-100g foods only. "Branded" (supermarket products) is
    // left out: it's hundreds of thousands of near-duplicate SKUs named in
    // capitals ("RICE", "RICE", ...), which buried real foods in search.
    // Survey (FNDDS) adds everyday dishes such as curries, dal and roti.
    // Each type is requested separately: USDA's API rejects some searches
    // (e.g. "coconut") with a 400 when several types are combined.
    private static readonly string[] GenericDataTypes = { "Foundation", "SR Legacy", "Survey (FNDDS)" };
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
            var foods = new List<UsdaFoodItem>();
            var failures = new List<string>();

            // pageSize applies per data type, so a query imports up to 3x it.
            // The three requests run in parallel; results are merged in the
            // fixed type order so imports stay deterministic.
            async Task<(string DataType, List<UsdaFoodItem>? Foods, string? Error)> Fetch(string dataType)
            {
                var url = $"https://api.nal.usda.gov/fdc/v1/foods/search" +
                          $"?query={Uri.EscapeDataString(query)}&pageSize={pageSize}" +
                          $"&dataType={Uri.EscapeDataString(dataType)}&api_key={apiKey}";
                try
                {
                    var response = await _httpClient.GetFromJsonAsync<UsdaFoodSearchResponse>(url);
                    return (dataType, response?.Foods ?? new(), null);
                }
                catch (HttpRequestException ex)
                {
                    // One type failing shouldn't lose the others' results.
                    return (dataType, null, ex.Message);
                }
            }

            foreach (var (dataType, fetched, error) in await Task.WhenAll(GenericDataTypes.Select(Fetch)))
            {
                if (fetched != null) foods.AddRange(fetched);
                else failures.Add($"{dataType}: {error}");
            }

            if (failures.Count == GenericDataTypes.Length)
                throw new HttpRequestException(string.Join("; ", failures));
            if (failures.Count > 0)
                job.ErrorMessage = "Partly failed: " + string.Join("; ", failures);

            var uniqueFoods = foods.DistinctBy(f => f.FdcId).ToList();
            job.RecordsFetched = uniqueFoods.Count;

            foreach (var item in uniqueFoods)
            {
                try
                {
                    var wasInserted = await UpsertFoodAsync(item, dataSource.Id);
                    if (wasInserted == null) job.RecordsFailed++; // no energy value: skipped
                    else if (wasInserted.Value) job.RecordsInserted++;
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

    /// <returns>true if inserted, false if updated, null if skipped for having no energy value.</returns>
    private async Task<bool?> UpsertFoodAsync(UsdaFoodItem item, Guid dataSourceId)
    {
        var externalId = item.FdcId.ToString();

        decimal? Find(int nutrientId) =>
            item.FoodNutrients.FirstOrDefault(n => n.NutrientId == nutrientId)?.Value;

        // The first of these IDs that the food actually reports.
        decimal? FirstOf(params int[] nutrientIds) =>
            nutrientIds.Select(Find).FirstOrDefault(v => v.HasValue);

        // A food with no energy value at all would show as "0 kcal" and
        // quietly wreck daily totals, so it's skipped rather than guessed.
        var energy = FirstOf(NutrientId_Energy, NutrientId_EnergyAtwaterGeneral, NutrientId_EnergyAtwaterSpecific);
        if (energy == null) return null;

        var existing = await _db.Foods.FirstOrDefaultAsync(
            f => f.DataSourceId == dataSourceId && f.ExternalId == externalId);

        decimal NutrientValue(int nutrientId) => nutrientId switch
        {
            NutrientId_Energy => energy.Value,
            NutrientId_Sugar => FirstOf(NutrientId_Sugar, NutrientId_SugarNlea) ?? 0,
            _ => Find(nutrientId) ?? 0,
        };

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