using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Models.Admin;

namespace NutriPath.Api.Services.Admin;

/// <summary>An admin action that is not allowed; the message is safe to show.</summary>
public sealed class AdminRuleException(string message, int status = StatusCodes.Status409Conflict) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>
/// Suspension, session revocation and role changes, shared by the user and administrator
/// endpoints so the safety rules live in one place:
/// - nobody changes their own status or roles;
/// - the last active SuperAdmin can't be suspended or lose the role;
/// - every change rotates the security stamp, ending consumer and admin sessions.
/// </summary>
public sealed class AccountControl(NutriPathDbContext db, SecurityState securityState, AdminAudit audit)
{
    public async Task SuspendAsync(Guid actorId, Guid userId, string reason, CancellationToken ct)
    {
        RequireReason(reason);
        if (actorId == userId) throw new AdminRuleException("You can't suspend your own account.");
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct) ?? throw new KeyNotFoundException();
        if (user.IsSuspended) return;
        await EnsureNotLastSuperAdminAsync(userId, ct);
        user.IsSuspended = true;
        user.SuspendedAtUtc = DateTime.UtcNow;
        user.SuspensionReason = reason.Trim();
        await EndSessionsAsync(user, ct);
        await audit.WriteAsync("user.suspend", resourceType: "User", resourceId: userId.ToString(), reason: reason, cancellationToken: ct);
    }

    public async Task ReactivateAsync(Guid actorId, Guid userId, string reason, CancellationToken ct)
    {
        RequireReason(reason);
        if (actorId == userId) throw new AdminRuleException("You can't change your own account status.");
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct) ?? throw new KeyNotFoundException();
        if (!user.IsSuspended) return;
        user.IsSuspended = false;
        user.SuspendedAtUtc = null;
        user.SuspensionReason = null;
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        await db.SaveChangesAsync(ct);
        securityState.Invalidate(userId);
        await audit.WriteAsync("user.reactivate", resourceType: "User", resourceId: userId.ToString(), reason: reason, cancellationToken: ct);
    }

    public async Task RevokeSessionsAsync(Guid userId, string reason, CancellationToken ct)
    {
        RequireReason(reason);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct) ?? throw new KeyNotFoundException();
        await EndSessionsAsync(user, ct);
        await audit.WriteAsync("user.revoke_sessions", resourceType: "User", resourceId: userId.ToString(), reason: reason, cancellationToken: ct);
    }

    public async Task SetRolesAsync(Guid actorId, Guid userId, IReadOnlyCollection<string> roles, string reason, CancellationToken ct)
    {
        RequireReason(reason);
        if (actorId == userId) throw new AdminRuleException("You can't change your own roles.");
        if (roles.Any(r => !AdminRoles.IsValid(r))) throw new AdminRuleException("Unknown role.", StatusCodes.Status400BadRequest);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct) ?? throw new KeyNotFoundException();
        var current = await db.UserRoles.Where(r => r.UserId == userId).ToListAsync(ct);
        if (current.Any(r => r.Role == AdminRoles.SuperAdmin) && !roles.Contains(AdminRoles.SuperAdmin))
            await EnsureNotLastSuperAdminAsync(userId, ct);
        if (current.Count == 0 && roles.Count > 0)
            throw new AdminRuleException("Consumer accounts can't be made administrators. Send an invitation for a separate admin account.");

        db.UserRoles.RemoveRange(current.Where(r => !roles.Contains(r.Role)));
        foreach (var role in roles.Where(r => current.All(c => c.Role != r)).Distinct())
            db.UserRoles.Add(new UserRole { UserId = userId, Role = role, AssignedByUserId = actorId });
        await EndSessionsAsync(user, ct); // permissions live in tokens; force a fresh sign-in
        await audit.WriteAsync("admin.roles.set", resourceType: "User", resourceId: userId.ToString(),
            summary: $"Roles: {string.Join(", ", current.Select(r => r.Role).Order())} → {(roles.Count == 0 ? "none" : string.Join(", ", roles.Order()))}",
            reason: reason, cancellationToken: ct);
    }

    public async Task ResetMfaAsync(Guid actorId, Guid userId, string reason, CancellationToken ct)
    {
        RequireReason(reason);
        if (actorId == userId) throw new AdminRuleException("Use your recovery codes to regain access to your own account.");
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct) ?? throw new KeyNotFoundException();
        db.AdminMfa.RemoveRange(await db.AdminMfa.Where(m => m.UserId == userId).ToListAsync(ct));
        db.AdminRecoveryCodes.RemoveRange(await db.AdminRecoveryCodes.Where(c => c.UserId == userId).ToListAsync(ct));
        await EndSessionsAsync(user, ct);
        await audit.WriteAsync("admin.mfa.reset", resourceType: "User", resourceId: userId.ToString(), reason: reason, cancellationToken: ct);
    }

    private async Task EndSessionsAsync(User user, CancellationToken ct)
    {
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.RefreshTokenHash = null;
        user.RefreshTokenExpiresAtUtc = null;
        var sessions = await db.AdminSessions.Where(s => s.UserId == user.Id && s.RevokedAtUtc == null).ToListAsync(ct);
        foreach (var s in sessions)
        {
            s.RevokedAtUtc = DateTime.UtcNow;
            securityState.InvalidateSession(s.Id);
        }
        await db.SaveChangesAsync(ct);
        securityState.Invalidate(user.Id);
    }

    private async Task EnsureNotLastSuperAdminAsync(Guid userId, CancellationToken ct)
    {
        var isSuper = await db.UserRoles.AnyAsync(r => r.UserId == userId && r.Role == AdminRoles.SuperAdmin, ct);
        if (!isSuper) return;
        var others = await db.UserRoles.Where(r => r.Role == AdminRoles.SuperAdmin && r.UserId != userId)
            .Join(db.Users.Where(u => !u.IsSuspended), r => r.UserId, u => u.Id, (r, u) => u.Id).AnyAsync(ct);
        if (!others) throw new AdminRuleException("This is the last active SuperAdmin. Add another SuperAdmin first.");
    }

    private static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new AdminRuleException("Give a short reason (at least 5 characters).", StatusCodes.Status400BadRequest);
        if (reason.Length > 300) throw new AdminRuleException("Keep the reason under 300 characters.", StatusCodes.Status400BadRequest);
    }
}
