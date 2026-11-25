using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using NutriPath.Api.Data;
using NutriPath.Api.Models.Admin;

namespace NutriPath.Api.Services.Admin;

/// <summary>
/// Writes append-only audit records. There is deliberately no update or delete API. Callers pass
/// short, sanitized summaries: never API keys, passwords, tokens, MFA secrets, chat content or
/// health data.
/// </summary>
public sealed class AdminAudit(NutriPathDbContext db, IHttpContextAccessor http)
{
    public async Task WriteAsync(string action, string outcome = "Success", string? resourceType = null,
        string? resourceId = null, string? summary = null, string? reason = null, Guid? actorUserId = null,
        string? actorEmail = null, CancellationToken cancellationToken = default)
    {
        var user = http.HttpContext?.User;
        var actor = actorUserId ?? (Guid.TryParse(user?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null);
        db.AdminAuditLogs.Add(new AdminAuditLog
        {
            ActorUserId = actor,
            ActorEmail = Truncate(actorEmail ?? user?.FindFirstValue(JwtRegisteredClaimNames.Email), 256),
            ActorRoles = Truncate(user is null ? null : string.Join(",", user.FindAll(ClaimTypes.Role).Select(c => c.Value)), 200),
            Action = action,
            ResourceType = resourceType,
            ResourceId = Truncate(resourceId, 100),
            Outcome = outcome,
            CorrelationId = Truncate(http.HttpContext?.TraceIdentifier, 64),
            Summary = Truncate(summary, 1000),
            Reason = Truncate(reason, 500),
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}
