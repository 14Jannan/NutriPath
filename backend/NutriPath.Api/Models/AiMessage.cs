namespace NutriPath.Api.Models;

public enum AiMessageRole { User, Assistant }

public class AiMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public AiConversation? Conversation { get; set; }

    public AiMessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;

    // What was actually retrieved and sent to the model for THIS message —
    // stored so you can audit, later, exactly why the AI said what it said.
    // This is the difference between "the AI is a black box" and "we can
    // point to the exact grounding data behind any given answer."
    public string? RetrievedContextJson { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
