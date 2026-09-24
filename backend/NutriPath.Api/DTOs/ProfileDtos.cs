namespace NutriPath.Api.DTOs;

public record UpdateGoalsRequest(
    int Age, string Sex, decimal HeightCm, decimal WeightKg,
    string ActivityLevel, string Goal,
    List<string>? Allergies, List<string>? DietaryPreferences);

public record ProfileResponse(
    string Email, string FullName, bool EmailVerified,
    int Age, string Sex, decimal HeightCm, decimal WeightKg,
    string ActivityLevel, string Goal,
    int TargetCalories, int TargetProteinGrams, int TargetCarbsGrams, int TargetFatGrams, int TargetFiberGrams,
    List<string> Allergies, List<string> DietaryPreferences);
