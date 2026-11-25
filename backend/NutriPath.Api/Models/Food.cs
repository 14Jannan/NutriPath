using System.Linq.Expressions;

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

    // Comma-separated allergen tags (e.g. "peanuts, milk"). USDA search
    // results don't carry reliable structured allergens, so this is only
    // filled in for manually curated foods; null means "not tagged", not
    // "allergen-free".
    public string? Allergens { get; set; }

    // Set only for foods a user added themselves (e.g. a Sri Lankan dish
    // USDA doesn't have). Those are private: their values aren't verified,
    // so they're never shown to, or suggested for, anyone else.
    // Null means the shared, sourced catalog.
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// The one rule for which foods a user may see, search, log or be
    /// suggested: the shared catalog plus their own additions. Every Foods
    /// query that serves a user goes through this.
    /// </summary>
    // Archived foods disappear from search, logging and AI suggestions. Past meal logs keep
    // their nutrient snapshots, so archiving never changes historical totals.
    public static Expression<Func<Food, bool>> VisibleTo(Guid userId) =>
        f => !f.IsArchived && (f.CreatedByUserId == null || f.CreatedByUserId == userId);

    // Catalogue management (admin portal).
    public bool IsArchived { get; set; }
    // Reviewed by a nutrition reviewer; USDA sync will not overwrite it.
    public bool IsVerified { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(60)]
    public string? Category { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string? NameTamil { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string? NameSinhala { get; set; }

    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedAtUtc { get; set; } = DateTime.UtcNow;
}