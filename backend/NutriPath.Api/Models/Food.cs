namespace NutriPath.Api.Models;

public class Food
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    public Guid DataSourceId { get; set; }
    public DataSource? DataSource { get; set; }

    // The ID this food has in the SOURCE system (e.g. USDA's fdcId).
    // Combined with DataSourceId, this is how we detect "have we already
    // imported this exact food before?" on a re-sync, instead of creating
    // duplicates every time.
    public string ExternalId { get; set; } = string.Empty;

    public decimal ServingSizeGrams { get; set; } = 100m;
    public decimal Calories { get; set; }
    public decimal ProteinGrams { get; set; }
    public decimal CarbsGrams { get; set; }
    public decimal FatGrams { get; set; }
    public decimal FiberGrams { get; set; }
    public decimal SugarGrams { get; set; }
    public decimal SodiumMilligrams { get; set; }

    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedAtUtc { get; set; } = DateTime.UtcNow;
}