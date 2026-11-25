using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Authorization;
using NutriPath.Api.Data;
using NutriPath.Api.Models.Admin;
using NutriPath.Api.Services;
using NutriPath.Api.Services.Admin;

namespace NutriPath.Api.Controllers.Admin;

/// <summary>Read-only access to the append-only audit log. There is no update or delete endpoint.</summary>
[Route("api/admin/audit-logs")]
public sealed class AdminAuditController(NutriPathDbContext db, AdminAudit audit) : AdminControllerBase
{
    [HttpGet]
    [AdminPermission(Permissions.AuditRead)]
    public async Task<PagedResult<AdminAuditLog>> List(string? search, string? action, string? outcome, Guid? actor,
        DateTime? from, DateTime? to, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        (page, pageSize) = Paging(page, pageSize);
        var query = Filter(search, action, outcome, actor, from, to);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(a => a.AtUtc).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<AdminAuditLog>(items, total, page, pageSize);
    }

    [HttpGet("actions")]
    [AdminPermission(Permissions.AuditRead)]
    public async Task<IActionResult> Actions(CancellationToken ct) =>
        Ok(await db.AdminAuditLogs.AsNoTracking().Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync(ct));

    [HttpGet("export")]
    [AdminPermission(Permissions.AuditExport)]
    public async Task<IActionResult> Export(string? search, string? action, string? outcome, Guid? actor,
        DateTime? from, DateTime? to, CancellationToken ct)
    {
        var rows = await Filter(search, action, outcome, actor, from, to).OrderByDescending(a => a.AtUtc).Take(20_000).ToListAsync(ct);
        await audit.WriteAsync("audit.export", resourceType: "AdminAuditLog", summary: $"{rows.Count} rows exported", cancellationToken: ct);
        var csv = Csv(new[] { new object?[] { "Time", "Actor", "Roles", "Action", "ResourceType", "ResourceId", "Outcome", "Summary", "Reason", "CorrelationId" } }
            .Concat(rows.Select(a => new object?[] { a.AtUtc, a.ActorEmail, a.ActorRoles, a.Action, a.ResourceType, a.ResourceId, a.Outcome, a.Summary, a.Reason, a.CorrelationId })));
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"nutripath-audit-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private IQueryable<AdminAuditLog> Filter(string? search, string? action, string? outcome, Guid? actor, DateTime? from, DateTime? to)
    {
        var query = db.AdminAuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = SearchPatterns.Contains(search.Trim());
            query = query.Where(a => EF.Functions.ILike(a.Action, pattern, SearchPatterns.EscapeCharacter) ||
                                     (a.ActorEmail != null && EF.Functions.ILike(a.ActorEmail, pattern, SearchPatterns.EscapeCharacter)) ||
                                     (a.Summary != null && EF.Functions.ILike(a.Summary, pattern, SearchPatterns.EscapeCharacter)) ||
                                     (a.ResourceId != null && EF.Functions.ILike(a.ResourceId, pattern, SearchPatterns.EscapeCharacter)));
        }
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action == action);
        if (!string.IsNullOrWhiteSpace(outcome)) query = query.Where(a => a.Outcome == outcome);
        if (actor is { } id) query = query.Where(a => a.ActorUserId == id);
        if (from is { } f) query = query.Where(a => a.AtUtc >= f.ToUniversalTime());
        if (to is { } t) query = query.Where(a => a.AtUtc < t.ToUniversalTime().AddDays(1));
        return query;
    }
}
