using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;

namespace NutriPath.Api.Services;

/// <summary>
/// Fills an empty or near-empty food catalog with everyday foods from USDA
/// when the API starts, so search works out of the box instead of needing
/// manual syncs. Runs in the background (startup isn't delayed), only when
/// the catalog is small, and only if a USDA API key is configured.
/// Re-running is safe: the sync upserts by USDA id, so nothing duplicates.
/// </summary>
public class StarterFoodCatalogSeeder : BackgroundService
{
    // Below this many foods, the catalog is treated as "not seeded yet".
    public const int SeedThreshold = 300;
    // Per USDA data type (3 types), so up to 45 foods per search.
    private const int ResultsPerQuery = 15;

    // Everyday foods for a Sri Lankan diet that USDA covers well, plus
    // common staples.
    public static readonly string[] Queries =
    {
        // Staples
        "rice white cooked", "rice brown cooked", "rice parboiled", "bread white", "bread whole wheat",
        "roti", "chapati", "noodles cooked", "pasta cooked", "oats", "cassava", "potato boiled", "sweet potato",
        // Legumes (dhal and gram)
        "lentils cooked", "chickpeas cooked", "mung beans", "kidney beans", "soybeans",
        // Protein
        "egg", "chicken breast", "chicken curry", "fish tuna", "fish mackerel", "sardines", "shrimp", "beef",
        "lamb", "tofu",
        // Dairy
        "milk", "yogurt", "cheese", "butter",
        // Coconut
        "coconut milk", "coconut meat", "coconut oil",
        // Vegetables
        "pumpkin", "carrot", "green beans", "cabbage", "spinach", "okra", "eggplant", "tomato", "onion",
        "cucumber", "bitter gourd", "leeks",
        // Fruit
        "banana", "mango", "papaya", "pineapple", "jackfruit", "guava", "apple", "orange", "watermelon",
        "avocado",
        // Snacks and drinks
        "peanuts", "cashew nuts", "tea", "coffee", "biscuits", "ice cream", "sugar",
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<StarterFoodCatalogSeeder> _logger;

    public StarterFoodCatalogSeeder(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        IHostEnvironment environment,
        ILogger<StarterFoodCatalogSeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _environment = environment;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Tests run the real app in memory and must never call USDA.
        if (_environment.IsEnvironment("Testing")) return;

        if (string.IsNullOrWhiteSpace(_config["Usda:ApiKey"]))
        {
            _logger.LogInformation("Starter food catalog skipped: no Usda:ApiKey configured.");
            return;
        }

        try
        {
            // A scope per run: DbContext and the sync service are scoped,
            // while this service lives for the whole app.
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();
            var foodCount = await db.Foods.CountAsync(stoppingToken);
            if (foodCount >= SeedThreshold)
            {
                _logger.LogInformation("Starter food catalog not needed: {Count} foods already present.", foodCount);
                return;
            }

            _logger.LogInformation("Importing starter food catalog from USDA ({Queries} searches)...", Queries.Length);
            var sync = scope.ServiceProvider.GetRequiredService<IUsdaFoodSyncService>();

            foreach (var query in Queries)
            {
                if (stoppingToken.IsCancellationRequested) return;

                var job = await sync.SyncAsync(query, ResultsPerQuery);
                if (job.ErrorMessage != null)
                    _logger.LogWarning("Starter catalog: '{Query}' failed: {Error}", query, job.ErrorMessage);

                // Stay well inside USDA's rate limit (1,000 requests/hour).
                await Task.Delay(TimeSpan.FromMilliseconds(300), stoppingToken);
            }

            var total = await db.Foods.CountAsync(stoppingToken);
            _logger.LogInformation("Starter food catalog ready: {Count} foods.", total);
        }
        catch (OperationCanceledException)
        {
            // App shutting down mid-import; the next start carries on.
        }
        catch (Exception ex)
        {
            // Never take the API down over this; search just stays smaller.
            _logger.LogError(ex, "Starter food catalog import failed.");
        }
    }
}
