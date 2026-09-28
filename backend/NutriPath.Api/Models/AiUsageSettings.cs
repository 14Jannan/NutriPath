namespace NutriPath.Api.Models;

/// <summary>Per-user assistant allowance, overridable via the "AiUsage" config section.</summary>
public class AiUsageSettings
{
    // Roughly 10-15 questions: each one sends the user's data, reference
    // knowledge and recent messages, so it costs a few thousand tokens.
    public int TokenLimit { get; set; } = 40_000;

    // A rolling window: tokens count against the limit for this long.
    public int WindowHours { get; set; } = 5;
}
