using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NutriPath.Api.Authorization;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Models.Admin;
using NutriPath.Api.Services;
using NutriPath.Api.Services.Admin;

namespace NutriPath.Api.Controllers.Admin;

/// <summary>
/// Overview metrics from real records. Definitions:
/// - Active user (day): logged at least one meal item or made an assistant request that UTC day.
/// - Weekly active: the same over the last 7 days. AI metrics come from AiRequestMetrics, which
///   starts recording when the admin portal is deployed (earlier history is not reconstructed).
/// </summary>
[Route("api/admin/dashboard")]
public sealed class AdminDashboardController(NutriPathDbContext db, IEmbeddingService embeddings,
    SystemHealth health) : AdminControllerBase
{
    [HttpGet("overview")]
    [AdminPermission(Permissions.DashboardRead)]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var since30 = today.AddDays(-30);

        var users = new
        {
            total = await db.Users.CountAsync(ct),
            verified = await db.Users.CountAsync(u => u.EmailVerified, ct),
            suspended = await db.Users.CountAsync(u => u.IsSuspended, ct),
            administrators = await db.UserRoles.Select(r => r.UserId).Distinct().CountAsync(ct),
            newToday = await db.Users.CountAsync(u => u.CreatedAtUtc >= today, ct),
            new7d = await db.Users.CountAsync(u => u.CreatedAtUtc >= today.AddDays(-6), ct),
            new30d = await db.Users.CountAsync(u => u.CreatedAtUtc >= since30, ct),
            dailyActive = await ActiveUsersAsync(today, today.AddDays(1), ct),
            weeklyActive = await ActiveUsersAsync(today.AddDays(-6), today.AddDays(1), ct),
        };

        var metrics = await db.AiRequestMetrics.AsNoTracking().Where(m => m.AtUtc >= since30)
            .Select(m => new { m.AtUtc, m.Outcome, m.LatencyMs, m.TotalTokens, m.InputTokens, m.OutputTokens }).ToListAsync(ct);
        var ok = metrics.Where(m => m.Outcome == "Success").Select(m => m.LatencyMs).Order().ToList();
        var ai = new
        {
            today = metrics.Count(m => m.AtUtc >= today),
            thisMonth = await db.AiRequestMetrics.CountAsync(m => m.AtUtc >= monthStart, ct),
            last30Days = metrics.Count,
            successRate = metrics.Count == 0 ? (double?)null : Math.Round((double)ok.Count / metrics.Count, 3),
            averageLatencyMs = ok.Count == 0 ? (double?)null : Math.Round(ok.Average()),
            medianLatencyMs = ok.Count == 0 ? (int?)null : ok[ok.Count / 2],
            tokens30d = metrics.Sum(m => m.TotalTokens),
            inputTokens30d = metrics.Sum(m => m.InputTokens ?? 0),
            outputTokens30d = metrics.Sum(m => m.OutputTokens ?? 0),
            recordingSince = await db.AiRequestMetrics.MinAsync(m => (DateTime?)m.AtUtc, ct),
        };

        var approvedChunks = await db.KnowledgeChunks.CountAsync(c => c.Document != null && c.Document.Approved && c.Document.Language == "en", ct);
        int? indexed = null;
        string? coverageNote = null;
        if (db.Database.IsNpgsql() && embeddings.Enabled)
        {
            try
            {
                indexed = await KnowledgeVectors.CountIndexedApprovedChunksAsync(db, embeddings.Model);
            }
            catch (Exception ex) when (ex is Npgsql.NpgsqlException or InvalidOperationException)
            {
                coverageNote = "Vector storage unavailable (pgvector not installed or not enabled).";
            }
        }
        else coverageNote = embeddings.Enabled ? "Vector search needs PostgreSQL with pgvector." : "Embedding worker not configured (Knowledge:EmbeddingUrl); keyword retrieval only.";

        var knowledge = new
        {
            approvedDocuments = await db.KnowledgeDocuments.CountAsync(d => d.Status == KnowledgeStatus.Approved, ct),
            pendingReview = await db.KnowledgeDocuments.CountAsync(d => d.Status == KnowledgeStatus.PendingReview, ct),
            drafts = await db.KnowledgeDocuments.CountAsync(d => d.Status == KnowledgeStatus.Draft, ct),
            approvedChunks,
            embeddingModel = embeddings.Model,
            indexedChunks = indexed,
            coverage = indexed is { } i && approvedChunks > 0 ? Math.Round((double)i / approvedChunks, 3) : (double?)null,
            coverageNote,
        };

        var foods = new
        {
            total = await db.Foods.CountAsync(f => f.CreatedByUserId == null, ct),
            userCreated = await db.Foods.CountAsync(f => f.CreatedByUserId != null, ct),
        };

        var jobs = new
        {
            queued = await db.BackgroundJobs.CountAsync(j => j.Status == "Queued" || j.Status == "Running", ct),
            failed7d = await db.BackgroundJobs.CountAsync(j => j.Status == "Failed" && j.CreatedAtUtc >= today.AddDays(-6), ct),
        };

        var activity = await db.AdminAuditLogs.AsNoTracking().OrderByDescending(a => a.AtUtc).Take(8)
            .Select(a => new { a.AtUtc, a.ActorEmail, a.Action, a.Outcome, a.ResourceType, a.Summary }).ToListAsync(ct);
        var pending = await db.KnowledgeDocuments.AsNoTracking().Where(d => d.Status == KnowledgeStatus.PendingReview)
            .OrderBy(d => d.UpdatedAtUtc).Take(5).Select(d => new { d.Id, d.Title, d.Category, d.UpdatedAtUtc }).ToListAsync(ct);

        return Ok(new
        {
            generatedAtUtc = now,
            definitions = new
            {
                activeUser = "Logged a meal item or made an assistant request during the UTC day.",
                aiMetrics = "Recorded per assistant request since the admin portal was deployed.",
            },
            users, ai, knowledge, foods, jobs,
            health = await health.SummaryAsync(ct),
            recentActivity = activity,
            pendingApprovals = pending,
        });
    }

    /// <summary>Daily series for the charts. range: today, 7, 30, 90, or from/to (max 366 days).</summary>
    [HttpGet("trends")]
    [AdminPermission(Permissions.DashboardRead)]
    public async Task<IActionResult> Trends(string? range, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var end = (to?.ToUniversalTime().Date ?? DateTime.UtcNow.Date).AddDays(1);
        var start = range switch
        {
            "today" => end.AddDays(-1),
            "7" => end.AddDays(-7),
            "90" => end.AddDays(-90),
            _ when from is { } f => f.ToUniversalTime().Date,
            _ => end.AddDays(-30),
        };
        if (start >= end || (end - start).TotalDays > 366) return BadRequest(new ProblemDetails { Status = 400, Title = "Choose a range of 1–366 days." });

        var registrations = await db.Users.Where(u => u.CreatedAtUtc >= start && u.CreatedAtUtc < end)
            .GroupBy(u => u.CreatedAtUtc.Date).Select(g => new { day = g.Key, count = g.Count() }).ToListAsync(ct);
        var mealUsers = await db.MealItems.Where(i => i.LoggedAtUtc >= start && i.LoggedAtUtc < end)
            .Select(i => new { day = i.LoggedAtUtc.Date, i.Meal!.UserId }).Distinct().ToListAsync(ct);
        var aiUsers = await db.AiUsageRecords.Where(r => r.CreatedAtUtc >= start && r.CreatedAtUtc < end)
            .Select(r => new { day = r.CreatedAtUtc.Date, r.UserId }).Distinct().ToListAsync(ct);
        var active = mealUsers.Concat(aiUsers).Distinct().GroupBy(x => x.day).ToDictionary(g => g.Key, g => g.Count());
        var metrics = await db.AiRequestMetrics.AsNoTracking().Where(m => m.AtUtc >= start && m.AtUtc < end)
            .Select(m => new { m.AtUtc, m.Provider, m.Language, m.Outcome, m.LatencyMs, m.TotalTokens }).ToListAsync(ct);
        var approvals = await db.KnowledgeDocuments.Where(d => d.Status == KnowledgeStatus.Approved && d.ReviewedAtUtc != null)
            .Select(d => d.ReviewedAtUtc!.Value).ToListAsync(ct);
        var foods = await db.Foods.Where(f => f.CreatedByUserId == null).Select(f => f.ImportedAtUtc).ToListAsync(ct);

        var days = Enumerable.Range(0, (int)(end - start).TotalDays).Select(i => start.AddDays(i)).ToList();
        return Ok(new
        {
            from = start, to = end.AddDays(-1),
            series = days.Select(day =>
            {
                var m = metrics.Where(x => x.AtUtc.Date == day).ToList();
                var lat = m.Where(x => x.Outcome == "Success").Select(x => x.LatencyMs).Order().ToList();
                return new
                {
                    date = day,
                    registrations = registrations.FirstOrDefault(r => r.day == day)?.count ?? 0,
                    activeUsers = active.GetValueOrDefault(day),
                    aiRequests = m.Count,
                    aiFailures = m.Count(x => x.Outcome != "Success"),
                    tokens = m.Sum(x => x.TotalTokens),
                    p50LatencyMs = lat.Count == 0 ? (int?)null : lat[lat.Count / 2],
                    p95LatencyMs = lat.Count == 0 ? (int?)null : lat[(int)Math.Ceiling(lat.Count * .95) - 1],
                    approvedKnowledge = approvals.Count(a => a < day.AddDays(1)),
                    catalogueFoods = foods.Count(f => f < day.AddDays(1)),
                };
            }),
            providers = metrics.GroupBy(m => m.Provider).Select(g => new { provider = g.Key, requests = g.Count() }),
            languages = metrics.Where(m => m.Outcome == "Success").GroupBy(m => m.Language).Select(g => new { language = g.Key, requests = g.Count() }),
        });
    }

    private async Task<int> ActiveUsersAsync(DateTime start, DateTime end, CancellationToken ct)
    {
        var meal = await db.MealItems.Where(i => i.LoggedAtUtc >= start && i.LoggedAtUtc < end).Select(i => i.Meal!.UserId).Distinct().ToListAsync(ct);
        var ai = await db.AiUsageRecords.Where(r => r.CreatedAtUtc >= start && r.CreatedAtUtc < end).Select(r => r.UserId).Distinct().ToListAsync(ct);
        return meal.Union(ai).Count();
    }
}
