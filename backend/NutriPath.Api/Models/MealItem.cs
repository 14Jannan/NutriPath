namespace NutriPath.Api.Models;

public class MealItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MealId { get; set; }
    public Meal? Meal { get; set; }

    public Guid FoodId { get; set; }
    public Food? Food { get; set; }

    public decimal Servings { get; set; } = 1m;
}
