using Microsoft.Extensions.Caching.Memory;
using NutriPath.Api.DTOs;

namespace NutriPath.Api.Services;

public interface IFoodLookupService
{
    /// <summary>
    /// Searches the local catalog, and — if it has few matches — imports that
    /// term from USDA first, so the catalog grows with what people search for.
    /// </summary>
    Task<List<FoodSearchResultDto>> SearchAsync(Guid userId, string query, int page, int pageSize);
}

/// <summary>
/// The starter catalog can't anticipate every food someone will search for.
/// When a search finds fewer than <see cref="MinLocalResults"/> foods, this
/// asks USDA for the term once, stores what it finds (for every user), and
/// searches again. Each term is looked up at most once per
/// <see cref="LookupCooldown"/>, so repeat searches never hit USDA again
/// and a missing food (e.g. "kottu", which USDA doesn't have) costs one
/// lookup, not one per keystroke or per user.
/// </summary>
public class FoodLookupService : IFoodLookupService
{
    public const int MinLocalResults = 5;
    public static readonly TimeSpan LookupCooldown = TimeSpan.FromHours(12);
    private const int ResultsPerDataType = 15;
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(8);

    private readonly IFoodSearchService _search;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _recentLookups;
    private readonly IConfiguration _config;
    private readonly ILogger<FoodLookupService> _logger;

    public FoodLookupService(
        IFoodSearchService search,
        IServiceScopeFactory scopeFactory,
        IMemoryCache recentLookups,
        IConfiguration config,
        ILogger<FoodLookupService> logger)
    {
        _search = search;
        _scopeFactory = scopeFactory;
        _recentLookups = recentLookups;
        _config = config;
        _logger = logger;
    }

    public async Task<List<FoodSearchResultDto>> SearchAsync(Guid userId, string query, int page, int pageSize)
    {
        var results = await _search.SearchAsync(userId, query, page, pageSize);

        // Only the first page of a short, specific-enough search that found
        // little — later pages mean the user already has plenty to scroll.
        if (page != 1 || results.Count >= MinLocalResults || query.Length < 3) return results;
        if (string.IsNullOrWhiteSpace(_config["Usda:ApiKey"])) return results;

        var term = query.Trim().ToLowerInvariant();
        var cacheKey = "usda-lookup:" + term;
        if (_recentLookups.TryGetValue(cacheKey, out _)) return results;
        _recentLookups.Set(cacheKey, true, LookupCooldown);

        try
        {
            // Don't keep the user waiting long on a slow USDA; whatever was
            // imported by then (or later) is there for the next search.
            var lookup = ImportInOwnScopeAsync(term);
            if (await Task.WhenAny(lookup, Task.Delay(LookupTimeout)) != lookup)
            {
                _logger.LogWarning("USDA lookup for '{Term}' is slow; returning local results for now.", term);
                return results;
            }

            var job = await lookup;
            if (job.RecordsInserted == 0) return results; // USDA had nothing new
            return await _search.SearchAsync(userId, query, page, pageSize);
        }
        catch (Exception ex)
        {
            // A USDA outage must never break search.
            _logger.LogWarning(ex, "USDA lookup for '{Term}' failed.", term);
            return results;
        }
    }

    // The import gets its own scope (and so its own DbContext), so if it
    // outlives this request after a timeout, it finishes safely in the
    // background instead of using the request's disposed DbContext.
    private async Task<Models.SyncJob> ImportInOwnScopeAsync(string term)
    {
        using var scope = _scopeFactory.CreateScope();
        var usda = scope.ServiceProvider.GetRequiredService<IUsdaFoodSyncService>();
        return await usda.SyncAsync(term, ResultsPerDataType);
    }
}
