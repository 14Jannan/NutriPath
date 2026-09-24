namespace NutriPath.Api.Models;

public enum AiMessageRole { User, Assistant }

// Simpler than Phase 0's AiConversation + AiMessage split: one flat table
// of messages per user, ordered by time, is enough to reconstruct recent
// conversation without a separate conversation-grouping concept yet.
public class AiMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AiMessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
