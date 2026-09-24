namespace NutriPath.Api.DTOs;

public record MacroProgress(string Label, decimal Current, decimal Target);

public record DailyNutritionResponse(
    int TargetCalories, int EatenCalories, int RemainingCalories,
    List<MacroProgress> Macros);
