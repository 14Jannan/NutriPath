using Microsoft.Extensions.Options;
using NutriPath.Api.Models;

namespace NutriPath.Api.Services.Llm;

/// <summary>Exposes the existing <see cref="IGroqClient"/> through the provider-neutral interface.</summary>
public sealed class GroqLlmClient(IGroqClient groq, string model) : ILlmClient
{
    public GroqLlmClient(IGroqClient groq, IOptions<GroqSettings> settings) : this(groq, settings.Value.Model) { }

    public string Provider => AiSettings.Groq;
    public string Model => model;

    public async Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var history = request.History
            .Select(m => new GroqMessage { Role = m.Role == "user" ? "user" : "assistant", Content = m.Content })
            .ToList();
        // Groq's JSON mode takes no schema; the prompt states the shape and the
        // backend validates every reply either way.
        var reply = request.JsonSchema is null
            ? await groq.AskAsync(request.SystemPrompt, request.UserMessage, history)
            : await groq.AskJsonAsync(request.SystemPrompt, request.UserMessage, history);
        return new LlmResponse(reply.Text, reply.TotalTokens, Provider, Model);
    }
}
