using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.Models.Admin;

namespace NutriPath.Api.Services.Admin;

/// <summary>A feature flag the code actually checks. Unknown flags can't be created from the portal.</summary>
public sealed record FeatureFlagDefinition(string Key, string Name, string Description, bool Default, bool ClientVisible);

public static class FeatureFlags
{
    public const string Assistant = "assistant.enabled";
    public const string AssistantFeedback = "assistant.feedback";
    public const string Registration = "registration.enabled";
    public const string CustomFoods = "foods.customFoods";
    public const string PrivacyRequests = "privacy.selfServiceRequests";
    public const string UsdaSync = "usda.sync";

    public static readonly IReadOnlyList<FeatureFlagDefinition> All =
    [
        new(Assistant, "AI assistant", "Users can ask the nutrition assistant. Turning this off is the emergency stop for AI answers.", true, true),
        new(AssistantFeedback, "Answer feedback", "Users can rate assistant answers and report unsafe ones.", true, true),
        new(Registration, "New registrations", "New consumer accounts can be created in the app.", true, true),
        new(CustomFoods, "Custom foods", "Users can add their own foods to log.", true, true),
        new(PrivacyRequests, "Data requests in the app", "Users can request a copy of their data or account deletion from the app.", true, true),
        new(UsdaSync, "USDA imports", "Administrators can import foods from USDA FoodData Central.", true, false),
    ];
}

public sealed class FeatureFlagsDocument
{
    public Dictionary<string, bool> Flags { get; set; } = [];

    public bool IsEnabled(string key) =>
        Flags.TryGetValue(key, out var value) ? value : FeatureFlags.All.FirstOrDefault(f => f.Key == key)?.Default ?? false;

    public List<string> Validate() =>
        Flags.Keys.Where(k => FeatureFlags.All.All(f => f.Key != k)).Select(k => $"Unknown feature flag {k}.").ToList();
}

public sealed class SystemSettingsDocument
{
    /// <summary>Shown in the app and the portal while set (e.g. planned maintenance).</summary>
    public string? MaintenanceMessage { get; set; }
    public string? SupportEmail { get; set; }
    public int MetricsRetentionDays { get; set; } = 180;
    public int FeedbackRetentionDays { get; set; } = 365;
    public int AuditRetentionDays { get; set; } = 730;
    public int NotificationRetentionDays { get; set; } = 90;
    public int ExportExpiryDays { get; set; } = 7;

    public List<string> Validate()
    {
        var errors = new List<string>();
        if (MaintenanceMessage is { Length: > 300 }) errors.Add("The maintenance message must be at most 300 characters.");
        if (SupportEmail is { Length: > 0 } email && (!email.Contains('@') || email.Length > 200)) errors.Add("Enter a valid support email.");
        if (MetricsRetentionDays is < 30 or > 730) errors.Add("AI metrics retention must be 30–730 days.");
        if (FeedbackRetentionDays is < 30 or > 730) errors.Add("Feedback retention must be 30–730 days.");
        // Audit evidence is kept for at least a year.
        if (AuditRetentionDays is < 365 or > 3650) errors.Add("Audit retention must be 365–3,650 days.");
        if (NotificationRetentionDays is < 7 or > 365) errors.Add("Notification retention must be 7–365 days.");
        if (ExportExpiryDays is < 1 or > 30) errors.Add("Data exports must expire after 1–30 days.");
        return errors;
    }
}

/// <summary>
/// Extra operator guidance appended to the assistant's system prompt. The safety rules in
/// <see cref="AiPrompt"/> stay in code and always come first; guidance can't remove them.
/// </summary>
public sealed class PromptSettingsDocument
{
    public string? Guidance { get; set; }

    public List<string> Validate()
    {
        var errors = new List<string>();
        if (Guidance is { Length: > 2000 }) errors.Add("Guidance must be at most 2,000 characters.");
        if (Guidance is not null && Guidance.Contains("{{")) errors.Add("Guidance can't contain template placeholders.");
        if (Guidance is not null && Regex.IsMatch(Guidance, @"ignore (all|the|previous|above)", RegexOptions.IgnoreCase))
            errors.Add("Guidance can't tell the model to ignore its rules.");
        return errors;
    }
}

/// <summary>Cached reads of the platform documents (10 s), shared by the whole process.</summary>
public sealed class PlatformSettingsStore(IServiceScopeFactory scopes)
{
    public const string FlagsKey = "flags";
    public const string SystemKey = "system";
    public const string PromptKey = "prompt";
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(10);
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (object Doc, int Version, DateTime LoadedAt)> _cache = [];

    public FeatureFlagsDocument Flags => Get<FeatureFlagsDocument>(FlagsKey).Doc;
    public SystemSettingsDocument System => Get<SystemSettingsDocument>(SystemKey).Doc;
    public (PromptSettingsDocument Doc, int Version) Prompt => Get<PromptSettingsDocument>(PromptKey);

    public bool IsEnabled(string flag) => Flags.IsEnabled(flag);

    public (T Doc, int Version) Get<T>(string key) where T : new()
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.LoadedAt < CacheFor)
                return ((T)cached.Doc, cached.Version);
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();
            var row = db.RuntimeSettings.AsNoTracking().SingleOrDefault(s => s.Key == key);
            var doc = row is null ? new T() : JsonSerializer.Deserialize<T>(row.Json, AiRuntimeDocument.Json) ?? new T();
            _cache[key] = (doc!, row?.Version ?? 0, DateTime.UtcNow);
            return (doc, row?.Version ?? 0);
        }
    }

    public void Invalidate(string key)
    {
        lock (_gate) _cache.Remove(key);
    }
}

/// <summary>Versioned updates of a platform document, with history and rollback (like AI configuration).</summary>
public sealed class PlatformSettingsService(NutriPathDbContext db, PlatformSettingsStore store, AdminAudit audit)
{
    public static readonly IReadOnlyDictionary<string, (Type Type, string AuditAction)> Documents = new Dictionary<string, (Type, string)>
    {
        [PlatformSettingsStore.FlagsKey] = (typeof(FeatureFlagsDocument), "feature_flags.update"),
        [PlatformSettingsStore.SystemKey] = (typeof(SystemSettingsDocument), "system_settings.update"),
        [PlatformSettingsStore.PromptKey] = (typeof(PromptSettingsDocument), "prompt.update"),
    };

    public async Task<(T Doc, int Version)> GetAsync<T>(string key, CancellationToken ct) where T : new()
    {
        var row = await db.RuntimeSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Key == key, ct);
        return row is null ? (new T(), 0) : (JsonSerializer.Deserialize<T>(row.Json, AiRuntimeDocument.Json) ?? new T(), row.Version);
    }

    public async Task<int> UpdateAsync<T>(string key, T doc, List<string> errors, int expectedVersion, Guid actor, string? note, CancellationToken ct)
    {
        if (errors.Count > 0) throw new AdminRuleException(string.Join(" ", errors), StatusCodes.Status400BadRequest);
        var json = JsonSerializer.Serialize(doc, AiRuntimeDocument.Json);
        var row = await db.RuntimeSettings.SingleOrDefaultAsync(s => s.Key == key, ct);
        var current = row?.Version ?? 0;
        if (current != expectedVersion)
            throw new AdminRuleException("These settings were changed by someone else. Reload and try again.");
        var before = row?.Json ?? "{}";
        if (row is null) db.RuntimeSettings.Add(row = new RuntimeSetting { Key = key });
        row.Json = json;
        row.Version = current + 1;
        row.UpdatedAtUtc = DateTime.UtcNow;
        row.UpdatedByUserId = actor;
        db.RuntimeSettingVersions.Add(new RuntimeSettingVersion
        {
            Key = key, Version = row.Version, Json = json, CreatedByUserId = actor,
            Note = note is { Length: > 300 } ? note[..300] : note,
        });
        await db.SaveChangesAsync(ct);
        store.Invalidate(key);
        await audit.WriteAsync(Documents[key].AuditAction, resourceType: "RuntimeSetting", resourceId: $"{key}@{row.Version}",
            summary: Diff(before, json), reason: note, cancellationToken: ct);
        return row.Version;
    }

    public async Task<IReadOnlyList<RuntimeSettingVersion>> HistoryAsync(string key, CancellationToken ct) =>
        await db.RuntimeSettingVersions.AsNoTracking().Where(v => v.Key == key)
            .OrderByDescending(v => v.Version).Take(50).ToListAsync(ct);

    public async Task<string> VersionJsonAsync(string key, int version, CancellationToken ct) =>
        (await db.RuntimeSettingVersions.AsNoTracking().SingleOrDefaultAsync(v => v.Key == key && v.Version == version, ct)
            ?? throw new KeyNotFoundException()).Json;

    private static string Diff(string before, string after)
    {
        var text = before == after ? "No changes" : $"{Shorten(before)} → {Shorten(after)}";
        return text.Length > 1000 ? text[..1000] : text;
    }

    private static string Shorten(string json) => json.Length > 480 ? json[..480] + "…" : json;
}
