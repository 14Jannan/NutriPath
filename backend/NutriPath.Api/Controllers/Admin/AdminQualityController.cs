using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Authorization;
using NutriPath.Api.Data;
using NutriPath.Api.Models.Admin;
using NutriPath.Api.Services.Admin;

namespace NutriPath.Api.Controllers.Admin;

public sealed record FeedbackReviewRequest(string Status, string? Note);
public sealed record IncidentRequest(string Title, string Category, string Severity, string? Description, Guid? FeedbackId);
public sealed record IncidentUpdateRequest(string? Status, string? Severity, string? Category, Guid? AssignedToUserId, bool Unassign, string? Resolution);
public sealed record EvaluationStartRequest(string Dataset, string Mode, int TopK);

/// <summary>Answer feedback, safety incidents and retrieval evaluations.</summary>
[Route("api/admin")]
public sealed class AdminQualityController(NutriPathDbContext db, QualityService quality, AdminAudit audit,
    AdminNotifications notifications) : AdminControllerBase
{
    // ---- Feedback ----

    [HttpGet("feedback")]
    [AdminPermission(Permissions.FeedbackRead)]
    public async Task<IActionResult> Feedback(string? status, bool? helpful, string? reason, string? language,
        int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        (page, pageSize) = Paging(page, pageSize);
        var query = db.AnswerFeedback.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(f => f.Status == status);
        if (helpful is { } h) query = query.Where(f => f.Helpful == h);
        if (!string.IsNullOrWhiteSpace(reason)) query = query.Where(f => f.Reason == reason);
        if (!string.IsNullOrWhiteSpace(language)) query = query.Where(f => f.Language == language);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(f => f.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(f => new { f.Id, f.Helpful, f.Reason, f.Language, f.Provider, f.Status, f.CreatedAtUtc, f.ReviewedAtUtc, hasComment = f.CommentCiphertext != null })
            .ToListAsync(ct);
        return Ok(new PagedResult<object>(items, total, page, pageSize));
    }

    /// <summary>Counts for the last 30 days (no content).</summary>
    [HttpGet("feedback/summary")]
    [AdminPermission(Permissions.FeedbackRead)]
    public async Task<IActionResult> FeedbackSummary(CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var rows = await db.AnswerFeedback.AsNoTracking().Where(f => f.CreatedAtUtc >= since)
            .Select(f => new { f.Helpful, f.Reason, f.Language, f.Provider, f.Status }).ToListAsync(ct);
        return Ok(new
        {
            total = rows.Count, helpful = rows.Count(r => r.Helpful), notHelpful = rows.Count(r => !r.Helpful),
            unreviewed = await db.AnswerFeedback.CountAsync(f => f.Status == "New", ct),
            byReason = rows.Where(r => !r.Helpful).GroupBy(r => r.Reason).Select(g => new { reason = g.Key, count = g.Count() }),
            byLanguage = rows.GroupBy(r => r.Language).Select(g => new { language = g.Key, helpful = g.Count(r => r.Helpful), notHelpful = g.Count(r => !r.Helpful) }),
            byProvider = rows.GroupBy(r => r.Provider ?? "unknown").Select(g => new { provider = g.Key, helpful = g.Count(r => r.Helpful), notHelpful = g.Count(r => !r.Helpful) }),
        });
    }

    /// <summary>The rated question and answer (decrypted). Every view is audited.</summary>
    [HttpGet("feedback/{id:guid}")]
    [AdminPermission(Permissions.FeedbackRead)]
    public async Task<IActionResult> FeedbackDetails(Guid id, CancellationToken ct)
    {
        var f = await db.AnswerFeedback.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        await audit.WriteAsync("feedback.view", resourceType: "AnswerFeedback", resourceId: id.ToString(), cancellationToken: ct);
        var incident = await db.SafetyIncidents.AsNoTracking().Where(i => i.FeedbackId == id).Select(i => new { i.Id, i.Status, i.Severity }).FirstOrDefaultAsync(ct);
        return Ok(new
        {
            f.Id, f.Helpful, f.Reason, f.Language, f.Provider, f.Status, f.ReviewNote, f.CreatedAtUtc, f.ReviewedAtUtc,
            question = quality.Decrypt(f.QuestionCiphertext), answer = quality.Decrypt(f.AnswerCiphertext), comment = quality.Decrypt(f.CommentCiphertext),
            reviewer = f.ReviewedByUserId is { } r ? await db.Users.Where(u => u.Id == r).Select(u => u.Email).SingleOrDefaultAsync(ct) : null,
            incident,
        });
    }

    [HttpPost("feedback/{id:guid}/review")]
    [AdminPermission(Permissions.FeedbackManage)]
    public async Task<IActionResult> Review(Guid id, [FromBody] FeedbackReviewRequest request, CancellationToken ct)
    {
        if (!QualityService.FeedbackStatuses.Contains(request.Status) || request.Status == "New")
            return BadRequest(new ProblemDetails { Status = 400, Title = "Choose Reviewed, Actioned or Dismissed." });
        var f = await db.AnswerFeedback.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        f.Status = request.Status;
        f.ReviewNote = request.Note?.Trim() is { Length: > 0 } n ? n[..Math.Min(500, n.Length)] : null;
        f.ReviewedByUserId = User.UserId();
        f.ReviewedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("feedback.review", resourceType: "AnswerFeedback", resourceId: id.ToString(), summary: request.Status, reason: f.ReviewNote, cancellationToken: ct);
        return NoContent();
    }

    // ---- Safety incidents ----

    [HttpGet("safety/incidents")]
    [AdminPermission(Permissions.SafetyRead)]
    public async Task<IActionResult> Incidents(string? status, string? severity, string? category, string? source,
        int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        (page, pageSize) = Paging(page, pageSize);
        var query = db.SafetyIncidents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) query = status == "Active" ? query.Where(i => i.Status == "Open" || i.Status == "Investigating") : query.Where(i => i.Status == status);
        if (!string.IsNullOrWhiteSpace(severity)) query = query.Where(i => i.Severity == severity);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(i => i.Category == category);
        if (!string.IsNullOrWhiteSpace(source)) query = query.Where(i => i.Source == source);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(i => i.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var people = await EmailsAsync(rows.Select(r => r.AssignedToUserId), ct);
        return Ok(new PagedResult<object>(rows.Select(i => (object)new
        {
            i.Id, i.Title, i.Source, i.Category, i.Severity, i.Status, i.Language, i.Provider, i.Model, i.CreatedAtUtc, i.UpdatedAtUtc, i.ResolvedAtUtc,
            assignedTo = i.AssignedToUserId is { } a ? people.GetValueOrDefault(a) : null, hasFeedback = i.FeedbackId != null,
        }).ToList(), total, page, pageSize));
    }

    [HttpGet("safety/summary")]
    [AdminPermission(Permissions.SafetyRead)]
    public async Task<IActionResult> SafetySummary(CancellationToken ct) => Ok(new
    {
        open = await db.SafetyIncidents.CountAsync(i => i.Status == "Open", ct),
        investigating = await db.SafetyIncidents.CountAsync(i => i.Status == "Investigating", ct),
        highOrCriticalActive = await db.SafetyIncidents.CountAsync(i => (i.Status == "Open" || i.Status == "Investigating") && (i.Severity == "High" || i.Severity == "Critical"), ct),
        blockedAllergyGuarantees30d = await db.SafetyIncidents.CountAsync(i => i.Source == "AutomaticValidation" && i.Category == "AllergySafety" && i.CreatedAtUtc >= DateTime.UtcNow.AddDays(-30), ct),
    });

    [HttpGet("safety/incidents/{id:guid}")]
    [AdminPermission(Permissions.SafetyRead)]
    public async Task<IActionResult> Incident(Guid id, CancellationToken ct)
    {
        var i = await db.SafetyIncidents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        var people = await EmailsAsync([i.AssignedToUserId, i.CreatedByUserId], ct);
        object? feedback = null;
        // The reported conversation is shown only to administrators who may read feedback.
        if (i.FeedbackId is { } fid && User.Has(Permissions.FeedbackRead))
        {
            var f = await db.AnswerFeedback.AsNoTracking().SingleOrDefaultAsync(x => x.Id == fid, ct);
            if (f is not null)
            {
                await audit.WriteAsync("feedback.view", resourceType: "AnswerFeedback", resourceId: fid.ToString(), summary: $"via incident {id}", cancellationToken: ct);
                feedback = new { f.Id, f.Reason, question = quality.Decrypt(f.QuestionCiphertext), answer = quality.Decrypt(f.AnswerCiphertext), comment = quality.Decrypt(f.CommentCiphertext) };
            }
        }
        var history = await db.AdminAuditLogs.AsNoTracking().Where(a => a.ResourceType == "SafetyIncident" && a.ResourceId == id.ToString())
            .OrderByDescending(a => a.AtUtc).Take(30).Select(a => new { a.AtUtc, a.ActorEmail, a.Action, a.Summary, a.Reason }).ToListAsync(ct);
        return Ok(new
        {
            i.Id, i.Title, i.Source, i.Category, i.Severity, i.Status, i.Description, i.Resolution, i.Language, i.Provider, i.Model,
            i.CreatedAtUtc, i.UpdatedAtUtc, i.ResolvedAtUtc, i.FeedbackId, i.AssignedToUserId,
            assignedTo = i.AssignedToUserId is { } a ? people.GetValueOrDefault(a) : null,
            createdBy = i.CreatedByUserId is { } c ? people.GetValueOrDefault(c) : i.Source == "AutomaticValidation" ? "Automatic validation" : "User report",
            feedback, history,
        });
    }

    [HttpPost("safety/incidents")]
    [AdminPermission(Permissions.SafetyManage)]
    public async Task<IActionResult> CreateIncident([FromBody] IncidentRequest request, CancellationToken ct)
    {
        Validate(request.Category, request.Severity, null);
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 300) return BadRequest(new ProblemDetails { Status = 400, Title = "Enter a title (up to 300 characters)." });
        if (request.FeedbackId is { } fid && !await db.AnswerFeedback.AnyAsync(f => f.Id == fid, ct)) throw new KeyNotFoundException();
        var incident = new SafetyIncident
        {
            Source = "Reviewer", Title = request.Title.Trim(), Category = request.Category, Severity = request.Severity,
            Description = request.Description?.Trim() is { Length: > 0 } d ? d[..Math.Min(2000, d.Length)] : null,
            FeedbackId = request.FeedbackId, CreatedByUserId = User.UserId(),
        };
        db.SafetyIncidents.Add(incident);
        if (incident.Severity is "High" or "Critical")
            notifications.Add("safety.created", $"{incident.Severity} safety incident: {incident.Title}", Permissions.SafetyRead, null, $"/safety/{incident.Id}", "danger");
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("safety.create", resourceType: "SafetyIncident", resourceId: incident.Id.ToString(), summary: $"{incident.Severity} {incident.Category}: {incident.Title}", cancellationToken: ct);
        return Ok(new { incident.Id });
    }

    [HttpPatch("safety/incidents/{id:guid}")]
    [AdminPermission(Permissions.SafetyManage)]
    public async Task<IActionResult> UpdateIncident(Guid id, [FromBody] IncidentUpdateRequest request, CancellationToken ct)
    {
        Validate(request.Category, request.Severity, request.Status);
        var i = await db.SafetyIncidents.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        if (request.Status is "Resolved" or "Dismissed" && string.IsNullOrWhiteSpace(request.Resolution ?? i.Resolution))
            return BadRequest(new ProblemDetails { Status = 400, Title = "Describe the resolution before closing the incident." });
        if (request.AssignedToUserId is { } assignee && !await db.UserRoles.AnyAsync(r => r.UserId == assignee, ct))
            return BadRequest(new ProblemDetails { Status = 400, Title = "Incidents can only be assigned to administrators." });
        var changes = new List<string>();
        void Set<T>(string name, T current, T? value, Action<T> apply) where T : class
        {
            if (value is not null && !Equals(current, value)) { changes.Add($"{name}: {current} → {value}"); apply(value); }
        }
        Set("status", i.Status, request.Status, v => i.Status = v);
        Set("severity", i.Severity, request.Severity, v => i.Severity = v);
        Set("category", i.Category, request.Category, v => i.Category = v);
        if (request.Resolution is { } res) { i.Resolution = res.Trim()[..Math.Min(2000, res.Trim().Length)]; changes.Add("resolution updated"); }
        if (request.Unassign) { i.AssignedToUserId = null; changes.Add("unassigned"); }
        else if (request.AssignedToUserId is { } to && to != i.AssignedToUserId) { i.AssignedToUserId = to; changes.Add("assigned"); }
        i.UpdatedAtUtc = DateTime.UtcNow;
        i.ResolvedAtUtc = i.Status is "Resolved" or "Dismissed" ? i.ResolvedAtUtc ?? DateTime.UtcNow : null;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("safety.update", resourceType: "SafetyIncident", resourceId: id.ToString(), summary: string.Join("; ", changes), cancellationToken: ct);
        return NoContent();
    }

    // ---- Evaluations ----

    [HttpGet("evaluations")]
    [AdminPermission(Permissions.AiEvaluate)]
    public async Task<IActionResult> Evaluations(CancellationToken ct)
    {
        var runs = await db.EvaluationRuns.AsNoTracking().OrderByDescending(r => r.CreatedAtUtc).Take(50)
            .Select(r => new { r.Id, r.Dataset, r.Mode, r.TopK, r.EmbeddingModel, r.Status, r.Error, r.CreatedAtUtc, r.FinishedAtUtc, r.MetricsJson, r.CreatedByUserId })
            .ToListAsync(ct);
        var people = await EmailsAsync(runs.Select(r => r.CreatedByUserId), ct);
        return Ok(new
        {
            datasets = EvaluationService.Datasets.Select(d => new { id = d, available = System.IO.File.Exists(EvaluationService.DatasetPath(d)) }),
            modes = EvaluationService.Modes,
            runs = runs.Select(r => new
            {
                r.Id, r.Dataset, r.Mode, r.TopK, r.EmbeddingModel, r.Status, r.Error, r.CreatedAtUtc, r.FinishedAtUtc,
                metrics = r.MetricsJson is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(r.MetricsJson),
                createdBy = r.CreatedByUserId is { } c ? people.GetValueOrDefault(c) : null,
            }),
        });
    }

    [HttpGet("evaluations/{id:guid}")]
    [AdminPermission(Permissions.AiEvaluate)]
    public async Task<IActionResult> Evaluation(Guid id, CancellationToken ct)
    {
        var r = await db.EvaluationRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        return Ok(new
        {
            r.Id, r.Dataset, r.Mode, r.TopK, r.EmbeddingModel, r.Status, r.Error, r.CreatedAtUtc, r.FinishedAtUtc,
            metrics = r.MetricsJson is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(r.MetricsJson),
            results = r.ResultsJson is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(r.ResultsJson),
        });
    }

    [HttpPost("evaluations")]
    [AdminPermission(Permissions.AiEvaluate)]
    public async Task<IActionResult> StartEvaluation([FromBody] EvaluationStartRequest request, [FromServices] EvaluationService evaluations, CancellationToken ct)
    {
        var run = await evaluations.StartAsync(request.Dataset, request.Mode, request.TopK <= 0 ? 5 : request.TopK, User.UserId(), ct);
        return Ok(new { run.Id, run.JobId });
    }

    private static void Validate(string? category, string? severity, string? status)
    {
        if (category is not null && !QualityService.Categories.Contains(category)) throw new AdminRuleException("Unknown category.", StatusCodes.Status400BadRequest);
        if (severity is not null && !QualityService.Severities.Contains(severity)) throw new AdminRuleException("Unknown severity.", StatusCodes.Status400BadRequest);
        if (status is not null && !QualityService.IncidentStatuses.Contains(status)) throw new AdminRuleException("Unknown status.", StatusCodes.Status400BadRequest);
    }

    private async Task<Dictionary<Guid, string>> EmailsAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.OfType<Guid>().Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
    }
}
