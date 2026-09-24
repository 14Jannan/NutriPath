namespace NutriPath.Api.Models;

public enum MealType { Breakfast, Lunch, Dinner, Snack }

public class Meal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public MealType Type { get; set; }

    // DateOnly, not DateTime: a meal's date is a calendar day with no
    // meaningful time component, so there's no midnight-UTC-vs-local
    // time portion to accidentally compare against.
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public List<MealItem> Items { get; set; } = new();
}
