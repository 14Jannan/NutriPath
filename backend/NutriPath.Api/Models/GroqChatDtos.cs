using System.Text.Json.Serialization;

namespace NutriPath.Api.Models;

// Groq mirrors OpenAI's chat completions request/response shape, so these
// DTOs are "OpenAI format" — a typed HttpClient is simpler than an SDK
// dependency for a single endpoint.

public class GroqChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<GroqMessage> Messages { get; set; } = new();

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.4; // lower = more consistent, less "creative" with numbers
}

public class GroqMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty; // "system", "user", or "assistant"

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

public class GroqChatResponse
{
    [JsonPropertyName("choices")]
    public List<GroqChoice> Choices { get; set; } = new();

    [JsonPropertyName("usage")]
    public GroqUsage? Usage { get; set; }
}

public class GroqUsage
{
    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}

/// <summary>The model's answer and how many tokens (prompt + answer) it cost.</summary>
public record GroqReply(string Text, int TotalTokens);

public class GroqChoice
{
    [JsonPropertyName("message")]
    public GroqMessage Message { get; set; } = new();
}
