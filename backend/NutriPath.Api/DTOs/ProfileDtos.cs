namespace NutriPath.Api.DTOs;

public record UpdateGoalsRequest(
    int Age, string Sex, decimal HeightCm, decimal WeightKg,
    string ActivityLevel, string Goal,
    List<string>? Allergies, List<string>? DietaryPreferences);

/// <summary>
/// Targets and body metrics calculated from unsaved goal values. BmiCategory is the
/// WHO adult category, or null under 18 (where adult categories don't apply).
/// </summary>
public record GoalsPreviewResponse(
    int TargetCalories, int TargetProteinGrams, int TargetCarbsGrams, int TargetFatGrams, int TargetFiberGrams,
    decimal Bmi, string? BmiCategory, int HealthyWeightMinKg, int HealthyWeightMaxKg);

/// <summary>The calculated preview plus the AI's explanation of it (Insight is null if the AI is unavailable).</summary>
public record ProfileInsightResponse(GoalsPreviewResponse Preview, string? Insight);

public record ProfileResponse(
    string Email, string FullName, bool EmailVerified,
    int Age, string Sex, decimal HeightCm, decimal WeightKg,
    string ActivityLevel, string Goal,
    int TargetCalories, int TargetProteinGrams, int TargetCarbsGrams, int TargetFatGrams, int TargetFiberGrams,
    List<string> Allergies, List<string> DietaryPreferences,
    // One of the fixed avatar IDs, or null if none chosen yet.
    string? AvatarId);

/// <summary>Sets the user's avatar; must be one of the fixed avatar IDs.</summary>
public record UpdateAvatarRequest(string AvatarId);
