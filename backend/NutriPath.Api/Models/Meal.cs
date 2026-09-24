namespace NutriPath.Api.Models;

public enum MealType { Breakfast, Lunch, Dinner, Snack }

public class Meal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public MealType MealType { get; set; }
    public DateOnly Date { get; set; }

    public List<MealItem> Items { get; set; } = new();
}