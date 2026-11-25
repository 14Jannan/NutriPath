using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Authorization;
using NutriPath.Api.Data;
using NutriPath.Api.Models.Admin;
using NutriPath.Api.Services;
using NutriPath.Api.Services.Admin;
using NutriPath.Api.Services.Multilingual;

namespace NutriPath.Api.Controllers.Admin;

public sealed record DocumentUpdate<T>(T Document, int ExpectedVersion, string? Note);
public sealed record DocumentRollback(int ExpectedVersion);
public sealed record DataRequestDecision(string? Note);

/// <summary>Feature flags, system settings, prompt guidance, terminology, notifications and privacy requests.</summary>
[Route("api/admin")]
public sealed class AdminPlatformController(NutriPathDbContext db, PlatformSettingsService settings, AdminAudit audit) : AdminControllerBase
{
    // ---- Feature flags ----

    [HttpGet("feature-flags")]
    [AdminPermission(Permissions.SystemMonitor)]
    public async Task<IActionResult> Flags(CancellationToken ct)
    {
        var (doc, version) = await settings.GetAsync<FeatureFlagsDocument>(PlatformSettingsStore.FlagsKey, ct);
        return Ok(new
        {
            version,
            flags = FeatureFlags.All.Select(f => new { f.Key, f.Name, f.Description, f.Default, f.ClientVisible, enabled = doc.IsEnabled(f.Key), overridden = doc.Flags.ContainsKey(f.Key) }),
        });
    }

    [HttpPut("feature-flags")]
    [AdminPermission(Permissions.FeatureFlagsManage)]
    public async Task<IActionResult> UpdateFlags([FromBody] DocumentUpdate<FeatureFlagsDocument> request, CancellationToken ct) =>
        Ok(new { version = await settings.UpdateAsync(PlatformSettingsStore.FlagsKey, request.Document, request.Document.Validate(), request.ExpectedVersion, User.UserId(), request.Note, ct) });

    // ---- System settings ----

    [HttpGet("settings")]
    [AdminPermission(Permissions.SystemMonitor)]
    public async Task<IActionResult> System(CancellationToken ct)
    {
        var (doc, version) = await settings.GetAsync<SystemSettingsDocument>(PlatformSettingsStore.SystemKey, ct);
        var lastRetention = await db.BackgroundJobs.AsNoTracking().Where(j => j.Type == BackgroundJobs.Retention)
            .OrderByDescending(j => j.CreatedAtUtc).Select(j => new { j.Status, j.FinishedAtUtc, j.ResultJson }).FirstOrDefaultAsync(ct);
        return Ok(new { version, settings = doc, lastRetention });
    }

    [HttpPut("settings")]
    [AdminPermission(Permissions.SystemSettingsManage)]
    public async Task<IActionResult> UpdateSystem([FromBody] DocumentUpdate<SystemSettingsDocument> request, CancellationToken ct)
    {
        var doc = request.Document;
        doc.MaintenanceMessage = string.IsNullOrWhiteSpace(doc.MaintenanceMessage) ? null : doc.MaintenanceMessage.Trim();
        doc.SupportEmail = string.IsNullOrWhiteSpace(doc.SupportEmail) ? null : doc.SupportEmail.Trim();
        return Ok(new { version = await settings.UpdateAsync(PlatformSettingsStore.SystemKey, doc, doc.Validate(), request.ExpectedVersion, User.UserId(), request.Note, ct) });
    }

    [HttpPost("settings/retention/run")]
    [AdminPermission(Permissions.SystemSettingsManage)]
    public async Task<IActionResult> RunRetention([FromServices] BackgroundJobs jobs, CancellationToken ct)
    {
        var id = await jobs.EnqueueAsync(BackgroundJobs.Retention, new { }, User.UserId(), ct);
        await audit.WriteAsync("retention.run", resourceType: "BackgroundJob", resourceId: id.ToString(), cancellationToken: ct);
        return Ok(new { jobId = id });
    }

    // ---- Prompt guidance ----

    /// <summary>The fixed rules (from code) per language and the current operator guidance.</summary>
    [HttpGet("prompts")]
    [AdminPermission(Permissions.AiProvidersRead)]
    public async Task<IActionResult> Prompts(CancellationToken ct)
    {
        var (doc, version) = await settings.GetAsync<PromptSettingsDocument>(PlatformSettingsStore.PromptKey, ct);
        return Ok(new
        {
            version, guidance = doc.Guidance,
            basePrompts = Languages.All.Select(l => new { language = l, prompt = AiPrompt.System(l, true) }),
            preview = AiPrompt.System(Languages.English, true, doc.Guidance),
        });
    }

    [HttpGet("platform/{key}/history")]
    [AdminPermission(Permissions.SystemMonitor)]
    public async Task<IActionResult> History(string key, CancellationToken ct)
    {
        if (!PlatformSettingsService.Documents.ContainsKey(key)) return NotFound();
        var rows = await settings.HistoryAsync(key, ct);
        var ids = rows.Select(r => r.CreatedByUserId).OfType<Guid>().Distinct().ToList();
        var people = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
        return Ok(rows.Select(v => new
        {
            v.Version, v.CreatedAtUtc, v.Note, document = JsonSerializer.Deserialize<JsonElement>(v.Json),
            createdBy = v.CreatedByUserId is { } c ? people.GetValueOrDefault(c) : null,
        }));
    }

    [HttpPut("prompts")]
    [AdminPermission(Permissions.AiPromptsManage)]
    [RequireRecentMfa]
    public async Task<IActionResult> UpdatePrompt([FromBody] DocumentUpdate<PromptSettingsDocument> request, CancellationToken ct)
    {
        var doc = request.Document;
        doc.Guidance = string.IsNullOrWhiteSpace(doc.Guidance) ? null : doc.Guidance.Trim();
        return Ok(new { version = await settings.UpdateAsync(PlatformSettingsStore.PromptKey, doc, doc.Validate(), request.ExpectedVersion, User.UserId(), request.Note, ct) });
    }

    /// <summary>Restores an earlier version as a new version (flags, system or prompt).</summary>
    [HttpPost("platform/{key}/rollback/{version:int}")]
    [AdminPermission(Permissions.SystemMonitor)]
    [RequireRecentMfa]
    public async Task<IActionResult> Rollback(string key, int version, [FromBody] DocumentRollback request, CancellationToken ct)
    {
        var required = key switch
        {
            PlatformSettingsStore.FlagsKey => Permissions.FeatureFlagsManage,
            PlatformSettingsStore.SystemKey => Permissions.SystemSettingsManage,
            PlatformSettingsStore.PromptKey => Permissions.AiPromptsManage,
            _ => null,
        };
        if (required is null) return NotFound();
        if (!User.Has(required)) return Forbid();
        var json = await settings.VersionJsonAsync(key, version, ct);
        var note = $"Rollback to version {version}";
        var newVersion = key switch
        {
            PlatformSettingsStore.FlagsKey => await Restore<FeatureFlagsDocument>(key, json, d => d.Validate(), request.ExpectedVersion, note, ct),
            PlatformSettingsStore.SystemKey => await Restore<SystemSettingsDocument>(key, json, d => d.Validate(), request.ExpectedVersion, note, ct),
            _ => await Restore<PromptSettingsDocument>(key, json, d => d.Validate(), request.ExpectedVersion, note, ct),
        };
        return Ok(new { version = newVersion });
    }

    private Task<int> Restore<T>(string key, string json, Func<T, List<string>> validate, int expected, string note, CancellationToken ct) where T : new()
    {
        var doc = JsonSerializer.Deserialize<T>(json, AiRuntimeDocument.Json) ?? new T();
        return settings.UpdateAsync(key, doc, validate(doc), expected, User.UserId(), note, ct);
    }

    // ---- Terminology ----

    [HttpGet("terminology")]
    [AdminPermission(Permissions.KnowledgeRead)]
    public async Task<IActionResult> Terms(string? status, string? language, string? search, CancellationToken ct)
    {
        var query = db.NutritionTerms.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(t => t.Status == status);
        if (!string.IsNullOrWhiteSpace(language)) query = query.Where(t => t.Language == language);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            query = query.Where(t => t.EnglishTerms.Contains(s) || t.RomanisedTerms.Contains(s) || t.NativeTerms.Contains(search.Trim()));
        }
        var rows = await query.OrderByDescending(t => t.CreatedAtUtc).Take(200).ToListAsync(ct);
        var ids = rows.SelectMany(r => new[] { r.CreatedByUserId, r.ApprovedByUserId }).OfType<Guid>().Distinct().ToList();
        var people = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
        return Ok(new
        {
            builtInEntries = NutritionTermLexicon.BuiltInCount,
            items = rows.Select(t => new
            {
                t.Id, t.Language, nativeTerms = TerminologyService.Lines(t.NativeTerms), romanisedTerms = TerminologyService.Lines(t.RomanisedTerms),
                t.EnglishTerms, t.Notes, t.Status, t.CreatedAtUtc, t.ApprovedAtUtc,
                author = t.CreatedByUserId is { } a ? people.GetValueOrDefault(a) : null,
                approver = t.ApprovedByUserId is { } b ? people.GetValueOrDefault(b) : null,
            }),
        });
    }

    /// <summary>Shows which English search terms a question maps to (built-in and approved terms).</summary>
    [HttpGet("terminology/test")]
    [AdminPermission(Permissions.KnowledgeRead)]
    public IActionResult TestTerms(string text) =>
        Ok(new { text, detected = LanguageResolver.Detect(text ?? "").Language, englishTerms = NutritionTermLexicon.EnglishTermsFor(text ?? "") });

    [HttpPost("terminology")]
    [AdminPermission(Permissions.TerminologyManage)]
    public async Task<IActionResult> CreateTerm([FromBody] TermInput input, [FromServices] TerminologyService terms, CancellationToken ct) =>
        Ok(new { (await terms.SaveAsync(null, input, User.UserId(), ct)).Id });

    [HttpPut("terminology/{id:guid}")]
    [AdminPermission(Permissions.TerminologyManage)]
    public async Task<IActionResult> EditTerm(Guid id, [FromBody] TermInput input, [FromServices] TerminologyService terms, CancellationToken ct) =>
        Ok(new { (await terms.SaveAsync(id, input, User.UserId(), ct)).Id });

    [HttpPost("terminology/{id:guid}/approve")]
    [AdminPermission(Permissions.TerminologyManage)]
    public async Task<IActionResult> ApproveTerm(Guid id, [FromServices] TerminologyService terms, CancellationToken ct)
    {
        await terms.ApproveAsync(id, User.UserId(), ct);
        return NoContent();
    }

    [HttpPost("terminology/{id:guid}/archive")]
    [AdminPermission(Permissions.TerminologyManage)]
    public async Task<IActionResult> ArchiveTerm(Guid id, [FromServices] TerminologyService terms, CancellationToken ct)
    {
        await terms.ArchiveAsync(id, ct);
        return NoContent();
    }

    // ---- Notifications ----

    [HttpGet("notifications")]
    [AdminAuthenticated]
    public async Task<IActionResult> Notifications([FromServices] AdminNotifications notifications, CancellationToken ct)
    {
        var (items, unread) = await notifications.ForAsync(User.UserId(), Permissions.For(User.Roles()), 30, ct);
        return Ok(new
        {
            unread,
            items = items.Select(x => new { x.Item.Id, x.Item.Type, x.Item.Title, x.Item.Body, x.Item.Link, x.Item.Tone, x.Item.CreatedAtUtc, read = x.Read }),
        });
    }

    [HttpPost("notifications/read")]
    [AdminAuthenticated]
    public async Task<IActionResult> MarkAllRead([FromServices] AdminNotifications notifications, CancellationToken ct)
    {
        await notifications.MarkReadAsync(User.UserId(), Permissions.For(User.Roles()), null, ct);
        return NoContent();
    }

    [HttpPost("notifications/{id:guid}/read")]
    [AdminAuthenticated]
    public async Task<IActionResult> MarkRead(Guid id, [FromServices] AdminNotifications notifications, CancellationToken ct)
    {
        await notifications.MarkReadAsync(User.UserId(), Permissions.For(User.Roles()), id, ct);
        return NoContent();
    }

    // ---- Privacy requests ----

    [HttpGet("privacy/requests")]
    [AdminPermission(Permissions.PrivacyRequestsRead)]
    public async Task<IActionResult> DataRequests(string? status, string? type, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        (page, pageSize) = Paging(page, pageSize);
        var query = db.DataRequests.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(r => r.Status == status);
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(r => r.Type == type);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(r => r.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new
            {
                r.Id, r.UserId, r.Type, r.Status, r.Email, r.UserNote, r.AdminNote, r.CreatedAtUtc, r.ProcessedAtUtc, r.ExportExpiresAtUtc,
                exportReady = r.ExportCiphertext != null, r.ProcessedByUserId,
                accountExists = db.Users.Any(u => u.Id == r.UserId),
                isAdministrator = db.UserRoles.Any(x => x.UserId == r.UserId),
            }).ToListAsync(ct);
        var ids = rows.Select(r => r.ProcessedByUserId).OfType<Guid>().Distinct().ToList();
        var people = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
        return Ok(new PagedResult<object>(rows.Select(r => (object)new
        {
            r.Id, r.UserId, r.Type, r.Status, email = r.Email ?? "(deleted account)", r.UserNote, r.AdminNote, r.CreatedAtUtc, r.ProcessedAtUtc,
            r.ExportExpiresAtUtc, r.exportReady, r.accountExists, r.isAdministrator,
            processedBy = r.ProcessedByUserId is { } p ? people.GetValueOrDefault(p) : null,
        }).ToList(), total, page, pageSize));
    }

    /// <summary>Approves a request: queues the export, or deletes the account immediately (irreversible).</summary>
    [HttpPost("privacy/requests/{id:guid}/approve")]
    [AdminPermission(Permissions.PrivacyRequestsManage)]
    [RequireRecentMfa]
    public async Task<IActionResult> ApproveRequest(Guid id, [FromBody] DataRequestDecision decision, [FromServices] DataRequestService requests, CancellationToken ct)
    {
        var type = await db.DataRequests.Where(r => r.Id == id).Select(r => r.Type).SingleOrDefaultAsync(ct) ?? throw new KeyNotFoundException();
        var result = await requests.ApproveAsync(id, User.UserId(), decision.Note, ct);
        await audit.WriteAsync(type == "Deletion" ? "privacy.deletion.complete" : "privacy.export.approve", resourceType: "DataRequest",
            resourceId: id.ToString(), reason: decision.Note, cancellationToken: ct);
        return Ok(result);
    }

    [HttpPost("privacy/requests/{id:guid}/reject")]
    [AdminPermission(Permissions.PrivacyRequestsManage)]
    public async Task<IActionResult> RejectRequest(Guid id, [FromBody] ReasonRequest request, [FromServices] DataRequestService requests, CancellationToken ct)
    {
        await requests.RejectAsync(id, User.UserId(), request.Reason, ct);
        await audit.WriteAsync("privacy.request.reject", resourceType: "DataRequest", resourceId: id.ToString(), reason: request.Reason, cancellationToken: ct);
        return NoContent();
    }
}
