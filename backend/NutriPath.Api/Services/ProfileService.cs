using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.DTOs;
using NutriPath.Api.Models;

namespace NutriPath.Api.Services;

/// <summary>
/// Calculates daily calorie and macro targets using standard, well-
/// established formulas — not invented numbers. The AI later EXPLAINS
/// these targets, but never calculates them itself.
/// </summary>
public class ProfileService : IProfileService
{
    private readonly NutriPathDbContext _db;

    public ProfileService(NutriPathDbContext db)
    {
        _db = db;
    }

    public async Task<ProfileResponse> GetMyProfileAsync(Guid userId)
    {
        var user = await _db.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("User not found.");
        return ToDto(user);
    }

    public async Task<ProfileResponse> UpdateGoalsAsync(Guid userId, UpdateGoalsRequest request)
    {
        // Out-of-range inputs would otherwise produce a negative BMR and
        // nonsense targets, so they're rejected before any calculation.
        if (request.Age is < 13 or > 120) throw new ArgumentException("Age must be between 13 and 120.");
        if (request.HeightCm is < 100 or > 250) throw new ArgumentException("Height must be between 100 and 250 cm.");
        if (request.WeightKg is < 25 or > 300) throw new ArgumentException("Weight must be between 25 and 300 kg.");

        if (!Enum.TryParse<Sex>(request.Sex, ignoreCase: true, out var sex))
            throw new ArgumentException("Sex must be Male, Female or Other.");
        if (!Enum.TryParse<ActivityLevel>(request.ActivityLevel, ignoreCase: true, out var activityLevel))
            throw new ArgumentException("Activity level must be Sedentary, Light, Moderate or VeryActive.");
        if (!Enum.TryParse<NutritionGoal>(request.Goal, ignoreCase: true, out var goal))
            throw new ArgumentException("Goal must be Lose, Maintain or Gain.");

        var user = await _db.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("User not found.");

        if (user.Profile == null)
        {
            user.Profile = new UserProfile { UserId = userId };
            _db.UserProfiles.Add(user.Profile);
        }

        var profile = user.Profile;
        profile.Age = request.Age;
        profile.Sex = sex;
        profile.HeightCm = request.HeightCm;
        profile.WeightKg = request.WeightKg;
        profile.ActivityLevel = activityLevel;
        profile.Goal = goal;
        profile.Allergies = Clean(request.Allergies);
        profile.DietaryPreferences = Clean(request.DietaryPreferences);

        CalculateTargets(profile);

        await _db.SaveChangesAsync();
        return ToDto(user);
    }

    /// <summary>
    /// Mifflin-St Jeor equation for BMR (calories burned at complete rest),
    /// scaled by an activity multiplier to get TDEE (Total Daily Energy
    /// Expenditure), then adjusted for the user's goal.
    /// </summary>
    private static void CalculateTargets(UserProfile p)
    {
        // Different constant for men vs women, per the original research.
        // "Other" uses the midpoint of the two, since the formula only
        // defines the binary cases.
        var sexConstant = p.Sex switch
        {
            Sex.Male => 5,
            Sex.Female => -161,
            _ => -78,
        };
        var bmr = (10 * (double)p.WeightKg) + (6.25 * (double)p.HeightCm) - (5 * p.Age) + sexConstant;

        var activityMultiplier = p.ActivityLevel switch
        {
            ActivityLevel.Sedentary => 1.2,
            ActivityLevel.Light => 1.375,
            ActivityLevel.Moderate => 1.55,
            ActivityLevel.VeryActive => 1.725,
            _ => 1.2,
        };

        var tdee = bmr * activityMultiplier;

        // A 500 kcal/day deficit or surplus is roughly 0.5kg of body weight
        // change per week (7,700 kcal ≈ 1kg of fat) — a sustainable rate.
        var targetCalories = p.Goal switch
        {
            NutritionGoal.Lose => tdee - 500,
            NutritionGoal.Gain => tdee + 500,
            _ => tdee,
        };

        // Floor so a small, sedentary "Lose" profile never gets a
        // crash-diet target below a safe minimum.
        targetCalories = Math.Max(targetCalories, 1200);

        p.TargetCalories = (int)Math.Round(targetCalories);

        // Protein: 1.6g per kg body weight — within the well-supported
        // 1.6-2.2g/kg range for muscle maintenance.
        p.TargetProteinGrams = (int)Math.Round((double)p.WeightKg * 1.6);

        // Fat: 25% of total calories (9 kcal per gram).
        p.TargetFatGrams = (int)Math.Round(targetCalories * 0.25 / 9);

        // Carbs: whatever calories remain after protein and fat (4 kcal/g).
        var proteinCalories = p.TargetProteinGrams * 4;
        var fatCalories = p.TargetFatGrams * 9;
        var remainingCalories = Math.Max(0, targetCalories - proteinCalories - fatCalories);
        p.TargetCarbsGrams = (int)Math.Round(remainingCalories / 4);

        // Fibre: 14g per 1,000 kcal — the USDA Dietary Guidelines figure.
        p.TargetFiberGrams = (int)Math.Round(targetCalories / 1000 * 14);
    }

    private static List<string> Clean(List<string>? values) =>
        (values ?? new List<string>())
            .Select(v => v.Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static ProfileResponse ToDto(User user)
    {
        // A user who registered but never set goals may have no profile
        // row yet — report empty values rather than failing.
        var p = user.Profile ?? new UserProfile();
        return new ProfileResponse(
            user.Email, p.FullName, user.EmailVerified,
            p.Age, p.Sex.ToString(), p.HeightCm, p.WeightKg,
            p.ActivityLevel.ToString(), p.Goal.ToString(),
            p.TargetCalories, p.TargetProteinGrams, p.TargetCarbsGrams, p.TargetFatGrams, p.TargetFiberGrams,
            p.Allergies, p.DietaryPreferences);
    }
}
