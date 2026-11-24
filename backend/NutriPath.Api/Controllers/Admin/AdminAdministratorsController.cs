using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Authorization;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Models.Admin;
using NutriPath.Api.Services;
using NutriPath.Api.Services.Admin;

namespace NutriPath.Api.Controllers.Admin;

public sealed record SetRolesRequest(IReadOnlyList<string> Roles, string Reason);
public sealed record InviteRequest(string Email, IReadOnlyList<string> Roles);
public sealed record AcceptInvitationRequest(string Token, string FullName, string Password);

/// <summary>Administrators, their roles, invitations and MFA recovery.</summary>
[Route("api/admin")]
public sealed class AdminAdministratorsController(NutriPathDbContext db, AccountControl accounts, AdminAudit audit,
    IPasswordHasher passwords, IConfiguration configuration) : AdminControllerBase
{
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(3);

    [HttpGet("administrators")]
    [AdminPermission(Permissions.AdminsRead)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var admins = await db.Users.AsNoTracking().Where(u => db.UserRoles.Any(r => r.UserId == u.Id))
            .Select(u => new
            {
                u.Id, u.Email, FullName = u.Profile != null ? u.Profile.FullName : "", u.IsSuspended, u.LastLoginAtUtc,
                u.CreatedAtUtc,
                Roles = db.UserRoles.Where(r => r.UserId == u.Id).Select(r => r.Role).ToList(),
                MfaEnabled = db.AdminMfa.Any(m => m.UserId == u.Id && m.Enabled),
                ActiveSessions = db.AdminSessions.Count(s => s.UserId == u.Id && s.RevokedAtUtc == null && s.ExpiresAtUtc > now),
                UnusedRecoveryCodes = db.AdminRecoveryCodes.Count(c => c.UserId == u.Id && c.UsedAtUtc == null),
            })
            .OrderBy(u => u.Email).ToListAsync(ct);
        return Ok(admins);
    }

    /// <summary>The role → permission matrix (defined in code).</summary>
    [HttpGet("roles")]
    [AdminPermission(Permissions.AdminsRead)]
    public IActionResult Roles() => Ok(new
    {
        roles = AdminRoles.All.Select(r => new { role = r, permissions = Permissions.ByRole[r] }),
        permissions = Permissions.All,
    });

    [HttpPut("administrators/{id:guid}/roles")]
    [AdminPermission(Permissions.AdminsAssignRoles)]
    [RequireRecentMfa]
    public async Task<IActionResult> SetRoles(Guid id, [FromBody] SetRolesRequest request, CancellationToken ct)
    {
        await accounts.SetRolesAsync(User.UserId(), id, request.Roles.Distinct().ToList(), request.Reason, ct);
        return NoContent();
    }

    /// <summary>Controlled MFA recovery: clears the authenticator so the admin enrols again at next sign-in.</summary>
    [HttpPost("administrators/{id:guid}/reset-mfa")]
    [AdminPermission(Permissions.AdminsManage)]
    [RequireRecentMfa]
    public async Task<IActionResult> ResetMfa(Guid id, [FromBody] ReasonRequest request, CancellationToken ct)
    {
        await accounts.ResetMfaAsync(User.UserId(), id, request.Reason, ct);
        return NoContent();
    }

    [HttpGet("administrators/invitations")]
    [AdminPermission(Permissions.AdminsRead)]
    public async Task<IActionResult> Invitations(CancellationToken ct) =>
        Ok(await db.AdminInvitations.AsNoTracking().OrderByDescending(i => i.CreatedAtUtc).Take(100)
            .Select(i => new
            {
                i.Id, i.Email, i.Roles, i.CreatedAtUtc, i.ExpiresAtUtc, i.AcceptedAtUtc, i.RevokedAtUtc,
                status = i.AcceptedAtUtc != null ? "Accepted" : i.RevokedAtUtc != null ? "Revoked"
                    : i.ExpiresAtUtc < DateTime.UtcNow ? "Expired" : "Pending",
            }).ToListAsync(ct));

    /// <summary>
    /// Creates a 3-day, single-use invitation and returns its link once. The link must be sent to
    /// the invitee through a trusted channel; only a hash of the token is stored.
    /// </summary>
    [HttpPost("administrators/invitations")]
    [AdminPermission(Permissions.AdminsManage)]
    [RequireRecentMfa]
    public async Task<IActionResult> Invite([FromBody] InviteRequest request, CancellationToken ct)
    {
        var email = request.Email?.Trim() ?? "";
        if (!email.Contains('@') || email.Length > 256) return Problem("Enter a valid email address.", statusCode: 400);
        var roles = request.Roles.Distinct().ToList();
        if (roles.Count == 0 || roles.Any(r => !AdminRoles.IsValid(r))) return Problem("Choose at least one valid role.", statusCode: 400);
        if (roles.Contains(AdminRoles.SuperAdmin) && !User.Roles().Contains(AdminRoles.SuperAdmin))
            return Forbid();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return Problem("An account with this email already exists. Administrators need a separate account.", statusCode: 409);

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var invitation = new AdminInvitation
        {
            Email = email, Roles = roles, TokenHash = AdminAuthService.Hash(token),
            ExpiresAtUtc = DateTime.UtcNow.Add(InvitationLifetime), CreatedByUserId = User.UserId(),
        };
        db.AdminInvitations.Add(invitation);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("admin.invite", resourceType: "AdminInvitation", resourceId: invitation.Id.ToString(),
            summary: $"{email} as {string.Join(", ", roles)}", cancellationToken: ct);
        var portal = (configuration["Admin:PortalUrl"] ?? $"{Request.Scheme}://{Request.Host}").TrimEnd('/');
        // The token is in the URL fragment, which browsers never send to servers or logs.
        return Ok(new { invitation.Id, link = $"{portal}/accept-invitation#{token}", invitation.ExpiresAtUtc });
    }

    [HttpDelete("administrators/invitations/{id:guid}")]
    [AdminPermission(Permissions.AdminsManage)]
    public async Task<IActionResult> RevokeInvitation(Guid id, CancellationToken ct)
    {
        var invitation = await db.AdminInvitations.SingleOrDefaultAsync(i => i.Id == id, ct);
        if (invitation is null) return NotFound();
        if (invitation.AcceptedAtUtc is null && invitation.RevokedAtUtc is null)
        {
            invitation.RevokedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync("admin.invite.revoke", resourceType: "AdminInvitation", resourceId: id.ToString(), cancellationToken: ct);
        }
        return NoContent();
    }

    /// <summary>Public: creates the invited administrator account. MFA enrolment follows at first sign-in.</summary>
    [HttpPost("auth/invitations/accept")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Accept([FromBody] AcceptInvitationRequest request, CancellationToken ct)
    {
        const string invalid = "This invitation is not valid. Ask an administrator for a new one.";
        if (string.IsNullOrWhiteSpace(request.Token)) return Problem(invalid, statusCode: 400);
        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Length > 100) return Problem("Enter your name.", statusCode: 400);
        if (request.Password is not { Length: >= AdminCli.MinimumPasswordLength and <= 200 })
            return Problem($"Use a password of at least {AdminCli.MinimumPasswordLength} characters.", statusCode: 400);
        var hash = AdminAuthService.Hash(request.Token);
        var invitation = await db.AdminInvitations.SingleOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (invitation is null || invitation.AcceptedAtUtc is not null || invitation.RevokedAtUtc is not null ||
            invitation.ExpiresAtUtc < DateTime.UtcNow || await db.Users.AnyAsync(u => u.Email == invitation.Email, ct))
            return Problem(invalid, statusCode: 400);

        var user = new User { Email = invitation.Email, PasswordHash = passwords.Hash(request.Password), EmailVerified = true };
        user.Profile = new UserProfile { UserId = user.Id, FullName = request.FullName.Trim() };
        db.Users.Add(user);
        foreach (var role in invitation.Roles)
            db.UserRoles.Add(new UserRole { UserId = user.Id, Role = role, AssignedByUserId = invitation.CreatedByUserId });
        invitation.AcceptedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("admin.invite.accept", resourceType: "User", resourceId: user.Id.ToString(),
            summary: string.Join(", ", invitation.Roles), actorUserId: user.Id, actorEmail: user.Email, cancellationToken: ct);
        return Ok(new { email = user.Email });
    }

    private ObjectResult Problem(string detail, int statusCode) =>
        new(new ProblemDetails { Status = statusCode, Title = detail, Detail = detail }) { StatusCode = statusCode };
}
