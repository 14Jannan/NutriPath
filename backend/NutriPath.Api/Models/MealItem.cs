namespace NutriPath.Api.Models;

public class MealItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MealId { get; set; }
    public Meal? Meal { get; set; }

    public Guid FoodId { get; set; }
    public Food? Food { get; set; }

    public decimal QuantityGrams { get; set; }

    // SNAPSHOT — captured at the moment of logging, per our Phase 0
    // design decision. If a food's nutrient values are later corrected
    // by a re-sync (Phase 4), a meal logged last week must NOT silently
    // change its historical numbers. These fields are the actual truth
    // for this specific logged item, forever.
    public decimal CaloriesSnapshot { get; set; }
    public decimal ProteinGramsSnapshot { get; set; }
    public decimal CarbsGramsSnapshot { get; set; }
    public decimal FatGramsSnapshot { get; set; }
    public decimal FiberGramsSnapshot { get; set; }
    public decimal SugarGramsSnapshot { get; set; }
    public decimal SodiumMilligramsSnapshot { get; set; }

    public DateTime LoggedAtUtc { get; set; } = DateTime.UtcNow;
}