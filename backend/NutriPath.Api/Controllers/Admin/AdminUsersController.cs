using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Authorization;
using NutriPath.Api.Data;
using NutriPath.Api.Models.Admin;
using NutriPath.Api.Services;
using NutriPath.Api.Services.Admin;

namespace NutriPath.Api.Controllers.Admin;

public sealed record AdminUserRow(Guid Id, string FullName, string Email, IReadOnlyList<string> Roles, bool EmailVerified,
    string Status, DateTime CreatedAtUtc, DateTime? LastLoginAtUtc, string? PreferredLanguage, int AiTokens30d);

public sealed record UsageLimitRequest(int TokenLimit, int WindowHours, string Reason);

/// <summary>
/// Accounts: search, status and sessions. Deliberately excludes passwords, chat content,
/// allergies, body data and meal history.
/// </summary>
[Route("api/admin/users")]
public sealed class AdminUsersController(NutriPathDbContext db, AccountControl accounts, AdminAudit audit,
    IAuthService auth, Microsoft.Extensions.Options.IOptions<NutriPath.Api.Models.AiUsageSettings> usage) : AdminControllerBase
{
    /// <summary>Server-side paged list with search, filters and sorting.</summary>
    [HttpGet]
    [AdminPermission(Permissions.UsersRead)]
    public async Task<PagedResult<AdminUserRow>> List(string? search, string? status, string? role, string? language,
        DateTime? registeredFrom, DateTime? registeredTo, string? sort, int page = 1, int pageSize = 25,
        CancellationToken ct = default)
    {
        (page, pageSize) = Paging(page, pageSize);
        var query = Filter(search, status, role, language, registeredFrom, registeredTo);
        var total = await query.CountAsync(ct);
        query = sort switch
        {
            "email" => query.OrderBy(u => u.Email),
            "lastLogin" => query.OrderByDescending(u => u.LastLoginAtUtc),
            "oldest" => query.OrderBy(u => u.CreatedAtUtc),
            _ => query.OrderByDescending(u => u.CreatedAtUtc),
        };
        var items = await Project(query.Skip((page - 1) * pageSize).Take(pageSize), ct);
        return new PagedResult<AdminUserRow>(items, total, page, pageSize);
    }

    [HttpGet("{id:guid}")]
    [AdminPermission(Permissions.UsersRead)]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var row = (await Project(db.Users.Where(u => u.Id == id), ct)).SingleOrDefault();
        if (row is null) return NotFound();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == id, ct);
        var since = DateTime.UtcNow.AddDays(-30);
        var aiByDay = await db.AiUsageRecords.Where(r => r.UserId == id && r.CreatedAtUtc > since)
            .GroupBy(r => r.CreatedAtUtc.Date).Select(g => new { date = g.Key, tokens = g.Sum(r => r.Tokens), requests = g.Count() })
            .OrderBy(x => x.date).ToListAsync(ct);
        var limit = await db.UserAiLimits.AsNoTracking().SingleOrDefaultAsync(l => l.UserId == id, ct);
        var adminSessions = await db.AdminSessions.CountAsync(s => s.UserId == id && s.RevokedAtUtc == null && s.ExpiresAtUtc > DateTime.UtcNow, ct);
        var activity = await db.AdminAuditLogs.AsNoTracking().Where(a => a.ResourceId == id.ToString())
            .OrderByDescending(a => a.AtUtc).Take(20)
            .Select(a => new { a.AtUtc, a.Action, a.Outcome, a.ActorEmail, a.Reason, a.Summary }).ToListAsync(ct);
        await audit.WriteAsync("user.view", resourceType: "User", resourceId: id.ToString(), cancellationToken: ct);
        return Ok(new
        {
            user = row,
            suspendedAtUtc = user.SuspendedAtUtc,
            suspensionReason = user.SuspensionReason,
            lockedUntilUtc = user.LockoutEndUtc > DateTime.UtcNow ? user.LockoutEndUtc : null,
            mobileSessionActive = user.RefreshTokenHash != null && user.RefreshTokenExpiresAtUtc > DateTime.UtcNow,
            adminSessions,
            aiUsageByDay = aiByDay,
            usageLimit = limit is null
                ? new { tokenLimit = usage.Value.TokenLimit, windowHours = usage.Value.WindowHours, overridden = false, reason = (string?)null }
                : new { tokenLimit = limit.TokenLimit, windowHours = limit.WindowHours, overridden = true, reason = limit.Reason },
            activity,
        });
    }

    [HttpPost("{id:guid}/suspend")]
    [AdminPermission(Permissions.UsersSuspend)]
    public async Task<IActionResult> Suspend(Guid id, [FromBody] ReasonRequest request, CancellationToken ct)
    {
        if (!await MayActOnAsync(id, ct)) return Forbid();
        await accounts.SuspendAsync(User.UserId(), id, request.Reason, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    [AdminPermission(Permissions.UsersSuspend)]
    public async Task<IActionResult> Reactivate(Guid id, [FromBody] ReasonRequest request, CancellationToken ct)
    {
        if (!await MayActOnAsync(id, ct)) return Forbid();
        await accounts.ReactivateAsync(User.UserId(), id, request.Reason, ct);
        return NoContent();
    }

    /// <summary>Ends every mobile and admin session of the account.</summary>
    [HttpPost("{id:guid}/revoke-sessions")]
    [AdminPermission(Permissions.UsersRevokeSessions)]
    public async Task<IActionResult> RevokeSessions(Guid id, [FromBody] ReasonRequest request, CancellationToken ct)
    {
        if (!await MayActOnAsync(id, ct)) return Forbid();
        await accounts.RevokeSessionsAsync(id, request.Reason, ct);
        return NoContent();
    }

    /// <summary>Emails the user the standard password-reset code. The admin never sees or sets the password.</summary>
    [HttpPost("{id:guid}/password-reset")]
    [AdminPermission(Permissions.UsersManage)]
    public async Task<IActionResult> TriggerPasswordReset(Guid id, CancellationToken ct)
    {
        if (!await MayActOnAsync(id, ct)) return Forbid();
        var email = await db.Users.Where(u => u.Id == id).Select(u => u.Email).SingleOrDefaultAsync(ct);
        if (email is null) return NotFound();
        await auth.ForgotPasswordAsync(new DTOs.ForgotPasswordRequest(email));
        await audit.WriteAsync("user.password_reset_sent", resourceType: "User", resourceId: id.ToString(), cancellationToken: ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/usage-limits")]
    [AdminPermission(Permissions.AiUsageManage)]
    public async Task<IActionResult> SetUsageLimit(Guid id, [FromBody] UsageLimitRequest request, CancellationToken ct)
    {
        if (request.TokenLimit is < 0 or > 5_000_000 || request.WindowHours is < 1 or > 24 * 31)
            return Problem("Token limit must be 0–5,000,000 and the window 1–744 hours.", statusCode: 400);
        if (string.IsNullOrWhiteSpace(request.Reason)) return Problem("A reason is required.", statusCode: 400);
        if (!await db.Users.AnyAsync(u => u.Id == id, ct)) return NotFound();
        var limit = await db.UserAiLimits.SingleOrDefaultAsync(l => l.UserId == id, ct);
        if (limit is null) db.UserAiLimits.Add(limit = new UserAiLimit { UserId = id });
        limit.TokenLimit = request.TokenLimit;
        limit.WindowHours = request.WindowHours;
        limit.Reason = request.Reason.Trim()[..Math.Min(300, request.Reason.Trim().Length)];
        limit.UpdatedAtUtc = DateTime.UtcNow;
        limit.UpdatedByUserId = User.UserId();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("user.ai_limit.set", resourceType: "User", resourceId: id.ToString(),
            summary: $"{request.TokenLimit} tokens / {request.WindowHours} h", reason: request.Reason, cancellationToken: ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/usage-limits")]
    [AdminPermission(Permissions.AiUsageManage)]
    public async Task<IActionResult> RemoveUsageLimit(Guid id, CancellationToken ct)
    {
        var limit = await db.UserAiLimits.SingleOrDefaultAsync(l => l.UserId == id, ct);
        if (limit is null) return NoContent();
        db.UserAiLimits.Remove(limit);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("user.ai_limit.removed", resourceType: "User", resourceId: id.ToString(), cancellationToken: ct);
        return NoContent();
    }

    /// <summary>CSV of the filtered list (basic account fields only). Audited.</summary>
    [HttpGet("export")]
    [AdminPermission(Permissions.UsersExport)]
    public async Task<IActionResult> Export(string? search, string? status, string? role, string? language,
        DateTime? registeredFrom, DateTime? registeredTo, CancellationToken ct)
    {
        var rows = await Project(Filter(search, status, role, language, registeredFrom, registeredTo)
            .OrderBy(u => u.CreatedAtUtc).Take(10_000), ct);
        await audit.WriteAsync("user.export", resourceType: "User", summary: $"{rows.Count} rows exported", cancellationToken: ct);
        var csv = Csv(new[] { new object?[] { "Id", "Name", "Email", "Roles", "Verified", "Status", "Registered", "LastLogin", "Language", "AiTokens30d" } }
            .Concat(rows.Select(r => new object?[] { r.Id, r.FullName, r.Email, string.Join(";", r.Roles), r.EmailVerified, r.Status, r.CreatedAtUtc, r.LastLoginAtUtc, r.PreferredLanguage, r.AiTokens30d })));
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"nutripath-users-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    // Acting on an administrator also needs admins.manage, so a UserAdmin can't suspend a SuperAdmin.
    private async Task<bool> MayActOnAsync(Guid id, CancellationToken ct) =>
        !await db.UserRoles.AnyAsync(r => r.UserId == id, ct) || User.Has(Permissions.AdminsManage);

    private IQueryable<Models.User> Filter(string? search, string? status, string? role, string? language,
        DateTime? from, DateTime? to)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = SearchPatterns.Contains(search.Trim());
            query = query.Where(u => EF.Functions.ILike(u.Email, pattern, SearchPatterns.EscapeCharacter) ||
                                     (u.Profile != null && EF.Functions.ILike(u.Profile.FullName, pattern, SearchPatterns.EscapeCharacter)));
        }
        var now = DateTime.UtcNow;
        query = status switch
        {
            "Active" => query.Where(u => !u.IsSuspended && u.EmailVerified),
            "Suspended" => query.Where(u => u.IsSuspended),
            "Unverified" => query.Where(u => !u.EmailVerified),
            "Locked" => query.Where(u => u.LockoutEndUtc > now),
            _ => query,
        };
        if (role == "User") query = query.Where(u => !db.UserRoles.Any(r => r.UserId == u.Id));
        else if (!string.IsNullOrWhiteSpace(role)) query = query.Where(u => db.UserRoles.Any(r => r.UserId == u.Id && r.Role == role));
        if (!string.IsNullOrWhiteSpace(language)) query = query.Where(u => u.Profile != null && u.Profile.PreferredLanguage == language);
        if (from is { } f) query = query.Where(u => u.CreatedAtUtc >= f.ToUniversalTime());
        if (to is { } t) query = query.Where(u => u.CreatedAtUtc < t.ToUniversalTime().AddDays(1));
        return query;
    }

    private async Task<List<AdminUserRow>> Project(IQueryable<Models.User> query, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var now = DateTime.UtcNow;
        var rows = await query.Select(u => new
        {
            u.Id, FullName = u.Profile != null ? u.Profile.FullName : "", u.Email, u.EmailVerified, u.IsSuspended,
            Locked = u.LockoutEndUtc > now, u.CreatedAtUtc, u.LastLoginAtUtc,
            Language = u.Profile != null ? u.Profile.PreferredLanguage : null,
            Tokens = db.AiUsageRecords.Where(r => r.UserId == u.Id && r.CreatedAtUtc > since).Sum(r => (int?)r.Tokens) ?? 0,
        }).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var roles = (await db.UserRoles.AsNoTracking().Where(r => ids.Contains(r.UserId)).ToListAsync(ct))
            .GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.Role).Order().ToList());
        return rows.Select(r => new AdminUserRow(r.Id, r.FullName, r.Email,
            roles.GetValueOrDefault(r.Id) ?? ["User"], r.EmailVerified,
            r.IsSuspended ? "Suspended" : r.Locked ? "Locked" : !r.EmailVerified ? "Unverified" : "Active",
            r.CreatedAtUtc, r.LastLoginAtUtc, r.Language, r.Tokens)).ToList();
    }
}
