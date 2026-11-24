using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using NutriPath.Api.Models;

namespace NutriPath.Api.Services.Llm;

/// <summary>
/// Google Gemini over the REST generateContent endpoint (v1beta), with structured JSON
/// output, bounded retries and no logging of prompt or answer text.
/// </summary>
public sealed class GeminiClient : ILlmClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Tamil and Sinhala as UTF-8 instead of \uXXXX escapes.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
    };

    private static readonly HashSet<HttpStatusCode> Retryable =
    [
        HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError, HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable, HttpStatusCode.GatewayTimeout,
    ];

    private readonly HttpClient _http;
    private readonly GeminiSettings _settings;
    private readonly ILogger<GeminiClient> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    [ActivatorUtilitiesConstructor]
    public GeminiClient(HttpClient http, IOptions<GeminiSettings> settings, ILogger<GeminiClient> logger)
        : this(http, settings, logger, Task.Delay) { }

    /// <summary>Test seam: <paramref name="delay"/> replaces Task.Delay between retries.</summary>
    public GeminiClient(HttpClient http, IOptions<GeminiSettings> settings, ILogger<GeminiClient> logger,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
        _delay = delay;
        _http.BaseAddress ??= new Uri(_settings.BaseUrl);
    }

    public string Provider => AiSettings.Gemini;
    public string Model => _settings.Model;

    public async Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            throw new HttpRequestException("Gemini is not configured.", null, HttpStatusCode.Unauthorized);

        var payload = JsonSerializer.Serialize(BuildBody(request, _settings), Json);
        var attempts = Math.Clamp(_settings.MaxAttempts, 1, 5);
        var maxDelay = TimeSpan.FromSeconds(Math.Clamp(_settings.MaxRetryDelaySeconds, 0, 60));

        for (var attempt = 1; ; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_settings.TimeoutSeconds, 5, 120)));
            using var message = new HttpRequestMessage(HttpMethod.Post,
                $"models/{Uri.EscapeDataString(_settings.Model)}:generateContent")
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
            };
            message.Headers.Add("x-goog-api-key", _settings.ApiKey);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(message, timeout.Token);
            }
            catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException &&
                                       !cancellationToken.IsCancellationRequested)
            {
                // Per-attempt timeout or connection failure: retry, then give up.
                _logger.LogWarning("Gemini request attempt {Attempt} failed ({ErrorType}).", attempt, ex.GetType().Name);
                if (attempt >= attempts) throw;
                await _delay(Backoff(attempt, maxDelay), cancellationToken);
                continue;
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                    return await ReadAsync(response, payload.Length, cancellationToken);

                var status = response.StatusCode;
                var hint = await RetryAfterAsync(response, cancellationToken);
                _logger.LogWarning("Gemini returned HTTP {StatusCode} on attempt {Attempt}.", (int)status, attempt);

                if (!Retryable.Contains(status))
                    throw new HttpRequestException($"Gemini returned HTTP {(int)status}.", null, status);

                // A hint longer than the allowed wait means the quota window is long
                // (e.g. a daily limit): stop now instead of holding the request open.
                var wait = hint ?? Backoff(attempt, maxDelay);
                if (attempt >= attempts || wait > maxDelay)
                {
                    if (status == HttpStatusCode.TooManyRequests) throw new LlmRateLimitedException(hint);
                    throw new HttpRequestException($"Gemini returned HTTP {(int)status}.", null, status);
                }
                await _delay(wait, cancellationToken);
            }
        }
    }

    public static object BuildBody(LlmRequest request, GeminiSettings settings)
    {
        var contents = request.History
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => new Content(m.Role == "user" ? "user" : "model", [new Part(m.Content)]))
            .Append(new Content("user", [new Part(request.UserMessage)]))
            .ToList();
        return new
        {
            systemInstruction = new { parts = new[] { new Part(request.SystemPrompt) } },
            contents,
            generationConfig = new
            {
                temperature = settings.Temperature,
                maxOutputTokens = Math.Clamp(settings.MaxOutputTokens, 64, 8192),
                responseMimeType = request.JsonSchema is null ? null : "application/json",
                responseJsonSchema = request.JsonSchema,
                thinkingConfig = string.IsNullOrWhiteSpace(settings.ThinkingLevel)
                    ? null
                    : new { thinkingLevel = settings.ThinkingLevel },
            },
        };
    }

    private async Task<LlmResponse> ReadAsync(HttpResponseMessage response, int promptChars, CancellationToken token)
    {
        var result = await response.Content.ReadFromJsonAsync<GenerateContentResponse>(token);
        var candidate = result?.Candidates?.FirstOrDefault();
        // Thought summaries are never part of the answer.
        var text = string.Concat(candidate?.Content?.Parts?.Where(p => p.Thought != true).Select(p => p.Text) ?? []);
        if (candidate?.FinishReason is { } reason && reason is not ("STOP" or "MAX_TOKENS"))
        {
            // SAFETY, RECITATION, PROHIBITED_CONTENT, ...: no usable answer. An empty
            // reply fails validation and the user gets the localized "couldn't verify" text.
            _logger.LogWarning("Gemini finished with reason {FinishReason}.", reason);
            text = "";
        }
        if (result?.PromptFeedback?.BlockReason is { } block)
        {
            _logger.LogWarning("Gemini blocked the prompt ({BlockReason}).", block);
            text = "";
        }
        var tokens = result?.UsageMetadata?.TotalTokenCount ?? (promptChars + text.Length) / 4;
        return new LlmResponse(text, tokens, Provider, result?.ModelVersion ?? Model);
    }

    public static TimeSpan Backoff(int attempt, TimeSpan max)
    {
        // 0.5 s, 1 s, 2 s, ... plus up to 250 ms of jitter, capped.
        var baseMs = 500 * Math.Pow(2, attempt - 1);
        var jitter = Random.Shared.Next(0, 250);
        return TimeSpan.FromMilliseconds(Math.Min(baseMs + jitter, max.TotalMilliseconds));
    }

    /// <summary>Reads the Retry-After header, or the RetryInfo "retryDelay" Google puts in the error body.</summary>
    public static async Task<TimeSpan?> RetryAfterAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.Headers.RetryAfter is { } header)
        {
            if (header.Delta is { } delta) return delta;
            if (header.Date is { } date) return date - DateTimeOffset.UtcNow;
        }
        try
        {
            var body = await response.Content.ReadAsStringAsync(token);
            if (body.Length is 0 or > 64 * 1024) return null;
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("error", out var error) ||
                !error.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var detail in details.EnumerateArray())
            {
                if (detail.TryGetProperty("retryDelay", out var delay) && delay.GetString() is { } text &&
                    text.EndsWith('s') &&
                    double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                    return TimeSpan.FromSeconds(seconds);
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    private sealed record Part([property: JsonPropertyName("text")] string Text);
    private sealed record Content(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("parts")] Part[] Parts);

    private sealed class GenerateContentResponse
    {
        [JsonPropertyName("candidates")] public List<Candidate>? Candidates { get; set; }
        [JsonPropertyName("usageMetadata")] public Usage? UsageMetadata { get; set; }
        [JsonPropertyName("promptFeedback")] public Feedback? PromptFeedback { get; set; }
        [JsonPropertyName("modelVersion")] public string? ModelVersion { get; set; }
    }

    private sealed class Candidate
    {
        [JsonPropertyName("content")] public CandidateContent? Content { get; set; }
        [JsonPropertyName("finishReason")] public string? FinishReason { get; set; }
    }

    private sealed class CandidateContent
    {
        [JsonPropertyName("parts")] public List<CandidatePart>? Parts { get; set; }
    }

    private sealed class CandidatePart
    {
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("thought")] public bool? Thought { get; set; }
    }

    private sealed class Usage
    {
        [JsonPropertyName("totalTokenCount")] public int? TotalTokenCount { get; set; }
    }

    private sealed class Feedback
    {
        [JsonPropertyName("blockReason")] public string? BlockReason { get; set; }
    }
}
