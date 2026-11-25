using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Models.Admin;

namespace NutriPath.Api.Services.Admin;

/// <summary>
/// Settings administrators may change at runtime. Null means "use the deployment configuration".
/// Privacy controls (Gemini:RealUserDataApproved, AI:UnapprovedUserProvider, synthetic account
/// domains) are intentionally absent: they can only change through deployment configuration.
/// </summary>
public sealed class AiRuntimeDocument
{
    public string? Provider { get; set; }
    public bool GeminiEnabled { get; set; } = true;
    public bool GroqEnabled { get; set; } = true;
    public string? GeminiModel { get; set; }
    public double? GeminiTemperature { get; set; }
    public int? GeminiMaxOutputTokens { get; set; }
    public string? GeminiThinkingLevel { get; set; }
    public int? GeminiTimeoutSeconds { get; set; }
    public int? GeminiMaxAttempts { get; set; }
    public int? GeminiMaxRetryDelaySeconds { get; set; }
    public string? GroqModel { get; set; }
    public bool? MultilingualEnabled { get; set; }
    public string? DefaultLanguage { get; set; }
    public string[]? SupportedLanguages { get; set; }
    public int? TokenLimit { get; set; }
    public int? WindowHours { get; set; }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Returns problems, or an empty list when the document is valid.</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (Provider is not (null or AiSettings.Gemini or AiSettings.Groq)) errors.Add("Provider must be Gemini or Groq.");
        if (Provider == AiSettings.Gemini && !GeminiEnabled) errors.Add("The active provider can't be disabled.");
        if (Provider == AiSettings.Groq && !GroqEnabled) errors.Add("The active provider can't be disabled.");
        if (GeminiModel is not null && !Regex.IsMatch(GeminiModel, "^gemini-[a-z0-9.-]{1,60}$")) errors.Add("Gemini model name is not valid.");
        if (GroqModel is not null && !Regex.IsMatch(GroqModel, "^[a-z0-9][a-z0-9._/-]{1,80}$")) errors.Add("Groq model name is not valid.");
        if (GeminiTemperature is < 0 or > 1) errors.Add("Temperature must be between 0 and 1.");
        if (GeminiMaxOutputTokens is < 256 or > 4096) errors.Add("Maximum output tokens must be 256–4096.");
        if (GeminiThinkingLevel is not (null or "" or "minimal" or "low" or "medium" or "high")) errors.Add("Thinking level must be minimal, low, medium or high.");
        if (GeminiTimeoutSeconds is < 5 or > 60) errors.Add("Timeout must be 5–60 seconds.");
        if (GeminiMaxAttempts is < 1 or > 5) errors.Add("Retry attempts must be 1–5.");
        if (GeminiMaxRetryDelaySeconds is < 0 or > 30) errors.Add("Retry delay must be 0–30 seconds.");
        var supported = SupportedLanguages?.Select(l => l.Trim().ToLowerInvariant()).Distinct().ToArray();
        if (supported is not null && (supported.Length == 0 || supported.Any(l => l is not ("en" or "ta" or "si")) || !supported.Contains("en")))
            errors.Add("Supported languages must include English and may add Tamil and Sinhala.");
        if (DefaultLanguage is not null && (DefaultLanguage is not ("en" or "ta" or "si") ||
                                            (supported is not null && !supported.Contains(DefaultLanguage))))
            errors.Add("The default language must be one of the supported languages.");
        if (TokenLimit is < 1000 or > 2_000_000) errors.Add("Token limit must be 1,000–2,000,000.");
        if (WindowHours is < 1 or > 168) errors.Add("Window must be 1–168 hours.");
        return errors;
    }
}

/// <summary>Cached, versioned settings documents read by the options post-configurators.</summary>
public sealed class RuntimeSettingsStore(IServiceScopeFactory scopes)
{
    public const string AiKey = "ai";
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(10);
    private readonly Lock _gate = new();
    private (AiRuntimeDocument Doc, int Version, DateTime LoadedAt)? _ai;

    public (AiRuntimeDocument Doc, int Version) Ai
    {
        get
        {
            lock (_gate)
            {
                if (_ai is { } cached && DateTime.UtcNow - cached.LoadedAt < CacheFor) return (cached.Doc, cached.Version);
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();
                var row = db.RuntimeSettings.AsNoTracking().SingleOrDefault(s => s.Key == AiKey);
                var doc = row is null ? new AiRuntimeDocument()
                    : JsonSerializer.Deserialize<AiRuntimeDocument>(row.Json, AiRuntimeDocument.Json) ?? new AiRuntimeDocument();
                _ai = (doc, row?.Version ?? 0, DateTime.UtcNow);
                return (doc, row?.Version ?? 0);
            }
        }
    }

    public void Invalidate()
    {
        lock (_gate) _ai = null;
    }
}

/// <summary>Applies the runtime document on top of deployment configuration, per request scope.</summary>
public sealed class AiRuntimeOverrides(RuntimeSettingsStore store) :
    IPostConfigureOptions<AiSettings>, IPostConfigureOptions<GeminiSettings>,
    IPostConfigureOptions<GroqSettings>, IPostConfigureOptions<AiUsageSettings>
{
    public void PostConfigure(string? name, AiSettings options)
    {
        var doc = store.Ai.Doc;
        if (doc.Provider is not null) options.Provider = doc.Provider;
        options.DisabledProviders = [.. new[] { doc.GeminiEnabled ? null : AiSettings.Gemini, doc.GroqEnabled ? null : AiSettings.Groq }.OfType<string>()];
        if (doc.MultilingualEnabled is { } enabled) options.Multilingual.Enabled = enabled;
        if (doc.SupportedLanguages is { Length: > 0 } languages) options.Multilingual.SupportedLanguages = languages;
        if (doc.DefaultLanguage is not null) options.Multilingual.DefaultLanguage = doc.DefaultLanguage;
    }

    public void PostConfigure(string? name, GeminiSettings options)
    {
        var doc = store.Ai.Doc;
        if (doc.GeminiModel is not null) options.Model = doc.GeminiModel;
        if (doc.GeminiTemperature is { } t) options.Temperature = t;
        if (doc.GeminiMaxOutputTokens is { } m) options.MaxOutputTokens = m;
        if (doc.GeminiThinkingLevel is not null) options.ThinkingLevel = doc.GeminiThinkingLevel;
        if (doc.GeminiTimeoutSeconds is { } s) options.TimeoutSeconds = s;
        if (doc.GeminiMaxAttempts is { } a) options.MaxAttempts = a;
        if (doc.GeminiMaxRetryDelaySeconds is { } d) options.MaxRetryDelaySeconds = d;
    }

    public void PostConfigure(string? name, GroqSettings options)
    {
        if (store.Ai.Doc.GroqModel is { } model) options.Model = model;
    }

    public void PostConfigure(string? name, AiUsageSettings options)
    {
        var doc = store.Ai.Doc;
        if (doc.TokenLimit is { } limit) options.TokenLimit = limit;
        if (doc.WindowHours is { } hours) options.WindowHours = hours;
    }
}

/// <summary>Versioned updates with optimistic concurrency, history and rollback.</summary>
public sealed class RuntimeSettingsService(NutriPathDbContext db, RuntimeSettingsStore store, AdminAudit audit)
{
    public async Task<(AiRuntimeDocument Doc, int Version)> GetAiAsync(CancellationToken ct)
    {
        var row = await db.RuntimeSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Key == RuntimeSettingsStore.AiKey, ct);
        return row is null ? (new AiRuntimeDocument(), 0)
            : (JsonSerializer.Deserialize<AiRuntimeDocument>(row.Json, AiRuntimeDocument.Json) ?? new(), row.Version);
    }

    public async Task<int> UpdateAiAsync(AiRuntimeDocument doc, int expectedVersion, Guid actor, string? note, CancellationToken ct)
    {
        var errors = doc.Validate();
        if (errors.Count > 0) throw new AdminRuleException(string.Join(" ", errors), StatusCodes.Status400BadRequest);
        doc.SupportedLanguages = doc.SupportedLanguages?.Select(l => l.Trim().ToLowerInvariant()).Distinct().ToArray();
        var json = JsonSerializer.Serialize(doc, AiRuntimeDocument.Json);
        var row = await db.RuntimeSettings.SingleOrDefaultAsync(s => s.Key == RuntimeSettingsStore.AiKey, ct);
        var current = row?.Version ?? 0;
        if (current != expectedVersion)
            throw new AdminRuleException("The configuration was changed by someone else. Reload and try again.");
        var before = row?.Json ?? "{}";
        if (row is null) db.RuntimeSettings.Add(row = new RuntimeSetting { Key = RuntimeSettingsStore.AiKey });
        row.Json = json;
        row.Version = current + 1;
        row.UpdatedAtUtc = DateTime.UtcNow;
        row.UpdatedByUserId = actor;
        db.RuntimeSettingVersions.Add(new RuntimeSettingVersion
        {
            Key = RuntimeSettingsStore.AiKey, Version = row.Version, Json = json, CreatedByUserId = actor,
            Note = note is { Length: > 300 } ? note[..300] : note,
        });
        await db.SaveChangesAsync(ct);
        store.Invalidate();
        await audit.WriteAsync("ai.configuration.update", resourceType: "RuntimeSetting", resourceId: $"ai@{row.Version}",
            summary: Diff(before, json), reason: note, cancellationToken: ct);
        return row.Version;
    }

    public async Task<IReadOnlyList<RuntimeSettingVersion>> HistoryAsync(CancellationToken ct) =>
        await db.RuntimeSettingVersions.AsNoTracking().Where(v => v.Key == RuntimeSettingsStore.AiKey)
            .OrderByDescending(v => v.Version).Take(50).ToListAsync(ct);

    /// <summary>Rollback creates a new version with an earlier version's content.</summary>
    public async Task<int> RollbackAiAsync(int version, int expectedVersion, Guid actor, CancellationToken ct)
    {
        var old = await db.RuntimeSettingVersions.AsNoTracking()
            .SingleOrDefaultAsync(v => v.Key == RuntimeSettingsStore.AiKey && v.Version == version, ct) ?? throw new KeyNotFoundException();
        var doc = JsonSerializer.Deserialize<AiRuntimeDocument>(old.Json, AiRuntimeDocument.Json) ?? new();
        return await UpdateAiAsync(doc, expectedVersion, actor, $"Rollback to version {version}", ct);
    }

    private static string Diff(string before, string after)
    {
        var a = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(before) ?? [];
        var b = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(after) ?? [];
        var changes = b.Where(kv => !a.TryGetValue(kv.Key, out var old) || old.ToString() != kv.Value.ToString())
            .Select(kv => $"{kv.Key}: {(a.TryGetValue(kv.Key, out var o) ? o.ToString() : "default")} → {kv.Value}");
        var text = string.Join("; ", changes);
        return text.Length == 0 ? "No changes" : text;
    }
}
