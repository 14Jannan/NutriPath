namespace NutriPath.Api.Services;

/// <summary>
/// The pure scoring formulas behind the weekly score. Public and
/// side-effect free so they can be unit-tested directly, without a
/// database or a running API.
/// </summary>
public static class ScoringRules
{
    // Scores 100 when exactly at target, dropping to 0 as the value moves
    // further than `tolerance` (as a fraction) away, in either direction —
    // under-eating is scored down just like over-eating.
    public static int ScoreCloseness(double actual, double target, double tolerance)
    {
        if (target <= 0) return 0;
        var deviation = Math.Abs(actual - target) / target;
        var score = 100 * (1 - deviation / tolerance);
        return (int)Math.Clamp(Math.Round(score), 0, 100);
    }

    // For things where more is fine (protein, fibre): meeting or exceeding
    // the target scores 100, falling short scores proportionally lower.
    public static int ScoreAtLeast(double actual, double target)
    {
        if (target <= 0) return 100;
        return (int)Math.Clamp(Math.Round(actual / target * 100), 0, 100);
    }

    // For things where less is better (sugar, sodium): under the limit
    // scores 100, dropping the further over the limit it goes.
    public static int ScorePenaltyAboveLimit(double actual, double limit)
    {
        if (actual <= limit) return 100;
        var overBy = (actual - limit) / limit;
        return (int)Math.Clamp(Math.Round(100 - overBy * 100), 0, 100);
    }

    public static string StatusFor(int score) => score switch
    {
        >= 90 => "Excellent",
        >= 75 => "Good",
        >= 60 => "Moderate",
        >= 40 => "Needs attention",
        _ => "Low",
    };

    public static string ToneFor(int score) => score >= 75 ? "good" : score >= 50 ? "neutral" : "warn";
}

/// <summary>
/// Foods are stored per ServingSizeGrams (e.g. per 100g); logging 150g of
/// a food stored per 100g scales every nutrient by 1.5x.
/// </summary>
public static class NutrientScaling
{
    public static decimal Scale(decimal perServing, decimal servingSizeGrams, decimal quantityGrams)
    {
        if (servingSizeGrams <= 0) throw new ArgumentOutOfRangeException(nameof(servingSizeGrams));
        return Math.Round(perServing * quantityGrams / servingSizeGrams, 1);
    }
}
