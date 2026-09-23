namespace NutriPath.Api.Models;

public enum Sex { Male, Female, Other }
public enum ActivityLevel { Sedentary, Light, Moderate, VeryActive }
public enum NutritionGoal { Lose, Maintain, Gain }

public class UserProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Foreign key: this column stores the Id of the User this profile
    // belongs to. EF Core figures out the relationship automatically
    // because the name "UserId" matches the convention <ClassName>Id.
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string FullName { get; set; } = string.Empty;

    public int Age { get; set; }
    public Sex Sex { get; set; }
    public decimal HeightCm { get; set; }
    public decimal WeightKg { get; set; }
    public ActivityLevel ActivityLevel { get; set; }
    public NutritionGoal Goal { get; set; }

    // Calculated by the backend in Phase 8 — never entered by hand.
    public int TargetCalories { get; set; }
    public int TargetProteinGrams { get; set; }
    public int TargetCarbsGrams { get; set; }
    public int TargetFatGrams { get; set; }
    public int TargetFiberGrams { get; set; }

    // Postgres (via the Npgsql EF Core provider) natively supports
    // storing an array in a single column — no separate join table
    // needed for a simple list of strings like this.
    public List<string> Allergies { get; set; } = new();
    public List<string> DietaryPreferences { get; set; } = new();
}