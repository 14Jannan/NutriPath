namespace NutriPath.Api.Models;

/// <summary>
/// Tokens one assistant reply cost, for the per-user usage limit. Kept apart
/// from the messages so deleting a chat doesn't give the tokens back.
/// </summary>
public class AiUsageRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public int Tokens { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
