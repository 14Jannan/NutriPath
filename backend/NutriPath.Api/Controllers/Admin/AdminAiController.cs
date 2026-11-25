using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NutriPath.Api.Authorization;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Models.Admin;
using NutriPath.Api.Services.Admin;

namespace NutriPath.Api.Controllers.Admin;

public sealed record AiConfigurationRequest(AiRuntimeDocument Configuration, int ExpectedVersion, string? Note);
public sealed record RollbackRequest(int ExpectedVersion);
public sealed record ReplaceKeyRequest(string ApiKey);

/// <summary>AI providers, models, runtime configuration, API keys and usage analytics.</summary>
[Route("api/admin")]
public sealed class AdminAiController(NutriPathDbContext db, RuntimeSettingsService settings,
    ProviderCredentialService credentials, IProviderCredentialStore store, IOptionsSnapshot<AiSettings> ai,
    IOptionsSnapshot<GeminiSettings> gemini, IOptionsSnapshot<GroqSettings> groq, IConfiguration configuration,
    IHttpClientFactory httpFactory, IMemoryCache cache) : AdminControllerBase
{
    [HttpGet("ai/providers")]
    [AdminPermission(Permissions.AiProvidersRead)]
    public async Task<IActionResult> Providers(CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddHours(-24);
        var metrics = await db.AiRequestMetrics.AsNoTracking().Where(m => m.AtUtc > since)
            .GroupBy(m => m.Provider)
            .Select(g => new
            {
                provider = g.Key, requests = g.Count(), failures = g.Count(m => m.Outcome != "Success"),
                rateLimited = g.Count(m => m.Outcome == "RateLimited"), tokens = g.Sum(m => m.TotalTokens),
                avgLatencyMs = g.Where(m => m.Outcome == "Success").Average(m => (double?)m.LatencyMs),
            }).ToListAsync(ct);
        var lastSuccess = await db.AiRequestMetrics.AsNoTracking().Where(m => m.Outcome == "Success")
            .GroupBy(m => m.Provider).Select(g => new { provider = g.Key, at = g.Max(m => m.AtUtc) }).ToListAsync(ct);
        var lastTests = await db.AdminAuditLogs.AsNoTracking().Where(a => a.Action == "credential.test")
            .GroupBy(a => a.ResourceId).Select(g => g.OrderByDescending(a => a.AtUtc).First()).ToListAsync(ct);

        object Provider(string name, string model, string privacy)
        {
            var m = metrics.FirstOrDefault(x => x.provider == name);
            var test = lastTests.FirstOrDefault(t => t.ResourceId == name);
            return new
            {
                name, model,
                enabled = !ai.Value.DisabledProviders.Contains(name),
                active = ai.Value.Provider == name,
                lastSuccessAtUtc = lastSuccess.FirstOrDefault(x => x.provider == name)?.at,
                lastTest = test is null ? null : new { test.AtUtc, test.Outcome, category = test.Summary },
                last24h = m is null ? null : new { m.requests, m.failures, m.rateLimited, m.tokens, avgLatencyMs = m.avgLatencyMs is { } l ? Math.Round(l) : (double?)null },
                privacy,
            };
        }
        var g = gemini.Value;
        var geminiPrivacy = !g.FreeTier ? "Paid tier configured: Google does not use prompts to improve its products."
            : g.RealUserDataApproved ? "Free tier with real-user processing approved in deployment configuration."
            : "Free tier: prompts may be used by Google to improve its products, so only synthetic or allow-listed accounts may use it.";
        return Ok(new
        {
            providers = new[]
            {
                Provider(AiSettings.Gemini, g.Model, geminiPrivacy),
                Provider(AiSettings.Groq, groq.Value.Model, "Existing provider for real users; no free-tier training restriction applied by NutriPath."),
            },
            privacy = new
            {
                geminiFreeTier = g.FreeTier,
                realUserDataApproved = g.RealUserDataApproved,
                syntheticDomains = g.SyntheticUserEmailDomains,
                approvedAccounts = g.ApprovedUserEmails.Length,
                unapprovedUserProvider = ai.Value.UnapprovedUserProvider,
                note = "These privacy controls change only through deployment configuration and a documented privacy review, not from the portal.",
            },
        });
    }

    /// <summary>Generation models the provider offers this key (cached 10 minutes), filtered to supported families.</summary>
    [HttpGet("ai/models")]
    [AdminPermission(Permissions.AiProvidersRead)]
    public async Task<IActionResult> Models([FromQuery] string provider, CancellationToken ct)
    {
        var name = ExternalProviders.Normalize(provider);
        if (name is not (ExternalProviders.Gemini or ExternalProviders.Groq)) return BadRequest();
        var models = await cache.GetOrCreateAsync(("models", name), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            var key = await store.GetActiveCredentialAsync(name, ct);
            if (key is null) return null;
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            try
            {
                if (name == ExternalProviders.Gemini)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, "https://generativelanguage.googleapis.com/v1beta/models?pageSize=200");
                    request.Headers.Add("x-goog-api-key", key);
                    using var response = await http.SendAsync(request, ct);
                    if (!response.IsSuccessStatusCode) return null;
                    var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                    return json.GetProperty("models").EnumerateArray()
                        .Where(m => m.GetProperty("supportedGenerationMethods").EnumerateArray().Any(x => x.GetString() == "generateContent"))
                        .Select(m => m.GetProperty("name").GetString()!.Replace("models/", ""))
                        .Where(n => n.Contains("flash") && !n.Contains("image") && !n.Contains("tts") && !n.Contains("omni"))
                        .Order().ToList();
                }
                using var groqRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.groq.com/openai/v1/models");
                groqRequest.Headers.Authorization = new("Bearer", key);
                using var groqResponse = await http.SendAsync(groqRequest, ct);
                if (!groqResponse.IsSuccessStatusCode) return null;
                var groqJson = await groqResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
                return groqJson.GetProperty("data").EnumerateArray().Select(m => m.GetProperty("id").GetString()!)
                    .Where(n => !n.Contains("whisper") && !n.Contains("guard") && !n.Contains("tts")).Order().ToList();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
                return null;
            }
        });
        return Ok(new { provider = name, available = models is not null, models = models ?? [] });
    }

    [HttpPost("ai/providers/{provider}/test")]
    [AdminPermission(Permissions.AiProvidersManage)]
    public async Task<IActionResult> Test(string provider, CancellationToken ct)
    {
        var name = ExternalProviders.Normalize(provider);
        if (name is null) return NotFound();
        return Ok(await credentials.TestActiveAsync(name, ct));
    }

    [HttpGet("ai/configuration")]
    [AdminPermission(Permissions.AiProvidersRead)]
    public async Task<IActionResult> Configuration(CancellationToken ct)
    {
        var (doc, version) = await settings.GetAiAsync(ct);
        return Ok(new
        {
            version,
            configuration = doc,
            effective = new
            {
                provider = ai.Value.Provider,
                disabledProviders = ai.Value.DisabledProviders,
                geminiModel = gemini.Value.Model, geminiTemperature = gemini.Value.Temperature,
                geminiMaxOutputTokens = gemini.Value.MaxOutputTokens, geminiThinkingLevel = gemini.Value.ThinkingLevel,
                geminiTimeoutSeconds = gemini.Value.TimeoutSeconds, geminiMaxAttempts = gemini.Value.MaxAttempts,
                geminiMaxRetryDelaySeconds = gemini.Value.MaxRetryDelaySeconds,
                groqModel = groq.Value.Model,
                multilingual = ai.Value.Multilingual,
                usage = HttpContext.RequestServices.GetRequiredService<IOptionsSnapshot<AiUsageSettings>>().Value,
            },
        });
    }

    [HttpPut("ai/configuration")]
    [AdminPermission(Permissions.AiModelsManage)]
    public async Task<IActionResult> UpdateConfiguration([FromBody] AiConfigurationRequest request, CancellationToken ct)
    {
        if (request.Configuration.Provider is not null && request.Configuration.Provider != ai.Value.Provider &&
            !User.Has(Permissions.AiProvidersManage))
            return Forbid();
        var version = await settings.UpdateAiAsync(request.Configuration, request.ExpectedVersion, User.UserId(), request.Note, ct);
        return Ok(new { version });
    }

    [HttpGet("ai/configuration/history")]
    [AdminPermission(Permissions.AiProvidersRead)]
    public async Task<IActionResult> History(CancellationToken ct) =>
        Ok((await settings.HistoryAsync(ct)).Select(v => new
        {
            v.Version, v.CreatedAtUtc, v.Note, configuration = JsonSerializer.Deserialize<JsonElement>(v.Json),
            createdBy = v.CreatedByUserId,
        }));

    [HttpPost("ai/configuration/rollback/{version:int}")]
    [AdminPermission(Permissions.AiModelsManage)]
    public async Task<IActionResult> Rollback(int version, [FromBody] RollbackRequest request, CancellationToken ct) =>
        Ok(new { version = await settings.RollbackAiAsync(version, request.ExpectedVersion, User.UserId(), ct) });

    // ---- API keys ----

    [HttpGet("credentials")]
    [AdminPermission(Permissions.AiProvidersRead)]
    public async Task<IActionResult> Credentials(CancellationToken ct)
    {
        var list = new List<object>();
        foreach (var provider in ExternalProviders.All) list.Add(await credentials.StatusAsync(provider, ct));
        return Ok(list);
    }

    /// <summary>
    /// Tests the candidate with a free request; only a working key is encrypted and activated.
    /// A failed candidate leaves the current key in place. Requires a recent second factor.
    /// </summary>
    [HttpPut("credentials/{provider}")]
    [AdminPermission(Permissions.AiSecretsRotate)]
    [RequireRecentMfa]
    public async Task<IActionResult> Replace(string provider, [FromBody] ReplaceKeyRequest request, CancellationToken ct)
    {
        var name = ExternalProviders.Normalize(provider);
        if (name is null) return NotFound();
        var result = await credentials.ReplaceAsync(name, request.ApiKey, User.UserId(), ct);
        cache.Remove(("models", name));
        return result.Ok ? Ok(result) : UnprocessableEntity(new ProblemDetails
        {
            Status = 422, Title = "The new key did not work, so the current key was kept.",
            Detail = result.Category switch
            {
                "InvalidCredential" => "The provider rejected this key.",
                "RateLimited" => "The provider is rate limiting this key right now. Try again later.",
                "Unreachable" => "The provider could not be reached.",
                _ => "The provider returned an error.",
            },
            Extensions = { ["category"] = result.Category },
        });
    }

    [HttpPost("credentials/{provider}/rollback/{id:guid}")]
    [AdminPermission(Permissions.AiSecretsRotate)]
    [RequireRecentMfa]
    public async Task<IActionResult> RollbackCredential(string provider, Guid id, CancellationToken ct)
    {
        var name = ExternalProviders.Normalize(provider);
        if (name is null) return NotFound();
        var result = await credentials.RollbackAsync(name, id, ct);
        return result.Ok ? Ok(result) : UnprocessableEntity(new ProblemDetails { Status = 422, Title = "That key version no longer works.", Extensions = { ["category"] = result.Category } });
    }

    [HttpPost("credentials/{provider}/revoke/{id:guid}")]
    [AdminPermission(Permissions.AiSecretsRotate)]
    [RequireRecentMfa]
    public async Task<IActionResult> Revoke(string provider, Guid id, [FromBody] ReasonRequest request, CancellationToken ct)
    {
        var name = ExternalProviders.Normalize(provider);
        if (name is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Reason)) return BadRequest(new ProblemDetails { Status = 400, Title = "A reason is required." });
        await credentials.RevokeAsync(name, id, request.Reason, ct);
        return NoContent();
    }

    // ---- Usage analytics ----

    /// <summary>Recorded request metadata. Costs are estimates from configured prices, not provider billing.</summary>
    [HttpGet("ai/usage")]
    [AdminPermission(Permissions.AiUsageRead)]
    public async Task<IActionResult> Usage(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var end = (to ?? DateTime.UtcNow.Date).ToUniversalTime().Date.AddDays(1);
        var start = (from ?? end.AddDays(-30)).ToUniversalTime().Date;
        if ((end - start).TotalDays > 366) return BadRequest(new ProblemDetails { Status = 400, Title = "Choose a range of at most one year." });
        var rows = await db.AiRequestMetrics.AsNoTracking().Where(m => m.AtUtc >= start && m.AtUtc < end)
            .Select(m => new { m.AtUtc, m.Provider, m.Model, m.Language, m.TotalTokens, m.InputTokens, m.OutputTokens, m.LatencyMs, m.RetrievalMs, m.Outcome, m.ValidationPassed, m.InsufficientEvidence, m.UserId })
            .ToListAsync(ct);
        var success = rows.Where(r => r.Outcome == "Success").ToList();
        var latencies = success.Select(r => r.LatencyMs).Order().ToList();
        double? Percentile(double p) => latencies.Count == 0 ? null : latencies[(int)Math.Ceiling(latencies.Count * p) - 1];
        decimal Price(string provider, string part) => configuration.GetValue<decimal>($"AI:Pricing:{provider}:{part}PerMillion");
        decimal Cost(string provider, int? input, int? output, int total) =>
            input is null || output is null
                ? total * Price(provider, "Input") / 1_000_000m
                : (input.Value * Price(provider, "Input") + output.Value * Price(provider, "Output")) / 1_000_000m;

        return Ok(new
        {
            from = start, to = end.AddDays(-1),
            totals = new
            {
                requests = rows.Count, succeeded = success.Count, failed = rows.Count - success.Count,
                rateLimited = rows.Count(r => r.Outcome == "RateLimited"), notPermitted = rows.Count(r => r.Outcome == "NotPermitted"),
                validationFallbacks = success.Count(r => !r.ValidationPassed),
                insufficientEvidence = success.Count(r => r.InsufficientEvidence),
                tokens = rows.Sum(r => r.TotalTokens), inputTokens = rows.Sum(r => r.InputTokens ?? 0), outputTokens = rows.Sum(r => r.OutputTokens ?? 0),
                latencyP50Ms = Percentile(.5), latencyP95Ms = Percentile(.95),
                retrievalP50Ms = success.Count == 0 ? (double?)null : success.Select(r => r.RetrievalMs).Order().ElementAt(success.Count / 2),
                estimatedCostUsd = Math.Round(rows.Sum(r => Cost(r.Provider, r.InputTokens, r.OutputTokens, r.TotalTokens)), 4),
                distinctUsers = rows.Select(r => r.UserId).Distinct().Count(),
            },
            byDay = rows.GroupBy(r => r.AtUtc.Date).OrderBy(g => g.Key).Select(g => new
            {
                date = g.Key, requests = g.Count(), failures = g.Count(r => r.Outcome != "Success"), tokens = g.Sum(r => r.TotalTokens),
                p50LatencyMs = g.Where(r => r.Outcome == "Success").Select(r => r.LatencyMs).Order().ElementAtOrDefault(g.Count(r => r.Outcome == "Success") / 2),
            }),
            byProvider = rows.GroupBy(r => r.Provider).Select(g => new { provider = g.Key, requests = g.Count(), tokens = g.Sum(r => r.TotalTokens), failures = g.Count(r => r.Outcome != "Success") }),
            byModel = success.GroupBy(r => r.Model).Select(g => new { model = g.Key, requests = g.Count(), tokens = g.Sum(r => r.TotalTokens) }),
            byLanguage = success.GroupBy(r => r.Language).Select(g => new { language = g.Key, requests = g.Count(), validationFallbacks = g.Count(r => !r.ValidationPassed) }),
            byOutcome = rows.GroupBy(r => r.Outcome).Select(g => new { outcome = g.Key, requests = g.Count() }),
            costNote = "Estimated from AI:Pricing settings (USD per million tokens). Free-tier usage is estimated at the configured price, usually 0. Check provider billing for actual charges.",
        });
    }
}
