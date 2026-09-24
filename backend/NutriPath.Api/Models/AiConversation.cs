namespace NutriPath.Api.Models;

public class AiConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public List<AiMessage> Messages { get; set; } = new();
}
