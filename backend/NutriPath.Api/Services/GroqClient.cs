using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using NutriPath.Api.Models;

namespace NutriPath.Api.Services;

public class GroqClient : IGroqClient
{
    private readonly HttpClient _httpClient;
    private readonly GroqSettings _settings;
    private readonly ILogger<GroqClient> _logger;

    public GroqClient(HttpClient httpClient, IOptions<GroqSettings> settings, ILogger<GroqClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_settings.BaseUrl);
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
    }

    public Task<GroqReply> AskAsync(string systemPrompt, string userMessage, IReadOnlyList<GroqMessage>? history = null) =>
        AskCoreAsync(systemPrompt, userMessage, history, false);

    public Task<GroqReply> AskJsonAsync(string systemPrompt, string userMessage, IReadOnlyList<GroqMessage> history) =>
        AskCoreAsync(systemPrompt, userMessage, history, true);

    private async Task<GroqReply> AskCoreAsync(string systemPrompt, string userMessage,
        IReadOnlyList<GroqMessage>? history, bool json)
    {
        var messages = new List<GroqMessage> { new() { Role = "system", Content = systemPrompt } };
        messages.AddRange(history ?? Array.Empty<GroqMessage>());
        messages.Add(new GroqMessage { Role = "user", Content = userMessage });

        var request = new GroqChatRequest
        {
            Model = _settings.Model, Messages = messages,
            ResponseFormat = json ? new GroqResponseFormat("json_object") : null,
        };

        // Relative path, no leading slash — a leading "/" would replace
        // BaseAddress's "/openai/v1/" path instead of appending to it.
        using var response = await _httpClient.PostAsJsonAsync("chat/completions", request);
        if (!response.IsSuccessStatusCode)
            _logger.LogWarning("Groq chat completion returned HTTP {StatusCode}.", (int)response.StatusCode);

        // Fail fast on a bad key or rate limit, rather than crashing
        // confusingly later when the parsed result turns out to be null.
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GroqChatResponse>();
        var text = result?.Choices.FirstOrDefault()?.Message.Content
            ?? "I couldn't generate a response right now.";

        // If Groq ever omits usage, estimate ~4 characters per token so the
        // limit still applies.
        var tokens = result?.Usage?.TotalTokens
            ?? (messages.Sum(m => m.Content.Length) + text.Length) / 4;
        return new GroqReply(text, tokens);
    }
}
