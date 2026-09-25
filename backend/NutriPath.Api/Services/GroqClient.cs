using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using NutriPath.Api.Models;

namespace NutriPath.Api.Services;

public class GroqClient : IGroqClient
{
    private readonly HttpClient _httpClient;
    private readonly GroqSettings _settings;

    public GroqClient(HttpClient httpClient, IOptions<GroqSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _httpClient.BaseAddress = new Uri(_settings.BaseUrl);
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
    }

    public async Task<string> AskAsync(string systemPrompt, string userMessage, IReadOnlyList<GroqMessage>? history = null)
    {
        var messages = new List<GroqMessage> { new() { Role = "system", Content = systemPrompt } };
        messages.AddRange(history ?? Array.Empty<GroqMessage>());
        messages.Add(new GroqMessage { Role = "user", Content = userMessage });

        var request = new GroqChatRequest { Model = _settings.Model, Messages = messages };

        // Relative path, no leading slash — a leading "/" would replace
        // BaseAddress's "/openai/v1/" path instead of appending to it.
        var response = await _httpClient.PostAsJsonAsync("chat/completions", request);

        // Fail fast on a bad key or rate limit, rather than crashing
        // confusingly later when the parsed result turns out to be null.
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GroqChatResponse>();
        return result?.Choices.FirstOrDefault()?.Message.Content
            ?? "I couldn't generate a response right now.";
    }
}
