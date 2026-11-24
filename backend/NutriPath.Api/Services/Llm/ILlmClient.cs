namespace NutriPath.Api.Services.Llm;

/// <summary>One earlier turn of the conversation. Role is "user" or "assistant".</summary>
public sealed record LlmMessage(string Role, string Content);

/// <param name="SystemPrompt">Instructions the model must follow.</param>
/// <param name="UserMessage">The current turn: structured data, evidence and the question.</param>
/// <param name="History">Earlier turns, oldest first.</param>
/// <param name="JsonSchema">When set, the reply must be JSON matching this schema.</param>
public sealed record LlmRequest(
    string SystemPrompt,
    string UserMessage,
    IReadOnlyList<LlmMessage> History,
    object? JsonSchema = null);

/// <summary>The model's text and the tokens it cost (prompt + output, including thinking).</summary>
public sealed record LlmResponse(string Text, int TotalTokens, string Provider, string Model);

/// <summary>A chat-completion provider (Gemini, Groq).</summary>
public interface ILlmClient
{
    string Provider { get; }
    string Model { get; }

    /// <summary>
    /// Throws <see cref="HttpRequestException"/> or <see cref="TaskCanceledException"/> when the provider
    /// is unavailable; <see cref="LlmRateLimitedException"/> when it keeps rate-limiting after retries.
    /// </summary>
    Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The provider rejected the request with HTTP 429 after all retries.</summary>
public sealed class LlmRateLimitedException(TimeSpan? retryAfter)
    : HttpRequestException("The AI provider is rate limiting requests.", null, System.Net.HttpStatusCode.TooManyRequests)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>No provider is permitted to process this user's data under the current configuration.</summary>
public sealed class LlmProviderNotPermittedException(string reason) : Exception(reason);
