using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NutriPath.Api.Data;

namespace NutriPath.Api.Services.Admin;

/// <summary>
/// Checks, on every authenticated request, that the account is not suspended and that the
/// token's security stamp is current (and, for admin tokens, that the session is not revoked).
/// Results are cached for 15 seconds; changes made on this instance invalidate the cache at once,
/// other instances pick them up within that window.
/// </summary>
public sealed class SecurityState(IMemoryCache cache, IServiceScopeFactory scopes)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(15);

    private sealed record UserState(string Stamp, bool Suspended);

    public void Invalidate(Guid userId) => cache.Remove(("user-state", userId));
    public void InvalidateSession(Guid sessionId) => cache.Remove(("admin-session", sessionId));

    public async Task<string?> RejectReasonAsync(ClaimsPrincipal principal, bool admin)
    {
        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(sub, out var userId)) return "invalid subject";

        var state = await cache.GetOrCreateAsync(("user-state", userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();
            return await db.Users.AsNoTracking().Where(u => u.Id == userId)
                .Select(u => new UserState(u.SecurityStamp, u.IsSuspended)).SingleOrDefaultAsync();
        });
        if (state is null) return "account not found";
        if (state.Suspended) return "account suspended";

        var stamp = principal.FindFirstValue(AdminTokenService.StampClaim);
        // Consumer tokens issued before stamps existed carry none; they expire within minutes.
        if ((admin || stamp is not null) && stamp != state.Stamp) return "stale security stamp";

        if (!admin) return null;
        if (!Guid.TryParse(principal.FindFirstValue(AdminTokenService.SessionClaim), out var sessionId))
            return "missing session";
        var active = await cache.GetOrCreateAsync(("admin-session", sessionId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();
            var now = DateTime.UtcNow;
            return await db.AdminSessions.AsNoTracking()
                .AnyAsync(s => s.Id == sessionId && s.UserId == userId && s.RevokedAtUtc == null && s.ExpiresAtUtc > now);
        });
        return active ? null : "session revoked";
    }

    /// <summary>JwtBearer hook: fails authentication when the account or session is no longer valid.</summary>
    public static JwtBearerEvents Events(bool admin) => new()
    {
        OnTokenValidated = async context =>
        {
            var state = context.HttpContext.RequestServices.GetRequiredService<SecurityState>();
            var reason = await state.RejectReasonAsync(context.Principal!, admin);
            if (reason is not null) context.Fail(reason);
        },
    };
}
