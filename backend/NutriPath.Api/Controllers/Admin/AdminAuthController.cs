using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Authorization;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Services.Admin;

namespace NutriPath.Api.Controllers.Admin;

public sealed record AdminLoginRequest(string Email, string Password);
public sealed record AdminMfaRequest(string Ticket, string? Code, string? RecoveryCode);
public sealed record AdminReauthRequest(string Password, string Code);

/// <summary>
/// Admin portal sign-in. Password → TOTP (enrolment on first sign-in) → session. The refresh
/// token lives only in an HttpOnly, SameSite=Strict cookie scoped to /api/admin/auth; the access
/// token is returned in the body and kept in memory by the portal.
/// </summary>
[ApiController]
[Route("api/admin/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminAuthController(AdminAuthService auth, WebAuthSettings webAuth, NutriPathDbContext db,
    SecurityState securityState, AdminAudit audit) : ControllerBase
{
    public const string RefreshCookie = "nutripath_admin_refresh";
    public const string CsrfHeader = "X-NutriPath-Admin";

    /// <summary>Step 1: email and password. Returns an MFA ticket (and, on first sign-in, the TOTP secret to enrol).</summary>
    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Login([FromBody] AdminLoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            return Problem("Email and password are required.", statusCode: 400);
        try
        {
            var result = await auth.LoginAsync(request.Email, request.Password, ct);
            return Ok(new { step = result.Step, ticket = result.MfaTicket, enrollmentSecret = result.EnrollmentSecret, otpAuthUri = result.OtpAuthUri });
        }
        catch (AdminAuthException ex)
        {
            return Problem(ex.Message, statusCode: ex.Status);
        }
    }

    /// <summary>Step 2: TOTP code (or recovery code). Starts the session; first enrolment also returns recovery codes once.</summary>
    [HttpPost("mfa/verify")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> VerifyMfa([FromBody] AdminMfaRequest request, CancellationToken ct)
    {
        if (!TrustedOrigin()) return Forbid();
        try
        {
            var session = await auth.VerifyMfaAsync(request.Ticket, request.Code, request.RecoveryCode,
                Request.Headers.UserAgent.ToString(), ct);
            return SessionResponse(session);
        }
        catch (AdminAuthException ex)
        {
            return Problem(ex.Message, statusCode: ex.Status);
        }
    }

    /// <summary>Rotates the refresh cookie and returns a new access token.</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        if (!TrustedSessionRequest()) return StatusCode(StatusCodes.Status403Forbidden);
        var token = Request.Cookies[RefreshCookie];
        if (string.IsNullOrWhiteSpace(token)) return Problem("Your session has ended. Please sign in again.", statusCode: 401);
        try
        {
            return SessionResponse(await auth.RefreshAsync(token, ct));
        }
        catch (AdminAuthException ex)
        {
            Response.Cookies.Delete(RefreshCookie, CookieOptions());
            return Problem(ex.Message, statusCode: ex.Status, extensions: ex.Code);
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (!TrustedSessionRequest()) return StatusCode(StatusCodes.Status403Forbidden);
        await auth.LogoutAsync(Request.Cookies[RefreshCookie], ct);
        Response.Cookies.Delete(RefreshCookie, CookieOptions());
        return NoContent();
    }

    /// <summary>Confirms password and a current TOTP code so sensitive actions are allowed for 15 minutes.</summary>
    [HttpPost("reauth")]
    [AdminAuthenticated]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Reauthenticate([FromBody] AdminReauthRequest request, CancellationToken ct)
    {
        try
        {
            var (token, expires) = await auth.ReauthenticateAsync(User.UserId(), User.SessionId(), request.Password, request.Code, ct);
            return Ok(new { accessToken = token, accessTokenExpiresAtUtc = expires });
        }
        catch (AdminAuthException ex)
        {
            return Problem(ex.Message, statusCode: ex.Status);
        }
    }

    [HttpGet("me")]
    [AdminAuthenticated]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == User.UserId(), ct);
        return Ok(await auth.ProfileAsync(user, ct: ct));
    }

    /// <summary>This administrator's active sessions.</summary>
    [HttpGet("sessions")]
    [AdminAuthenticated]
    public async Task<IActionResult> Sessions(CancellationToken ct)
    {
        var userId = User.UserId();
        var current = User.SessionId();
        var now = DateTime.UtcNow;
        var sessions = await db.AdminSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAtUtc == null && s.ExpiresAtUtc > now)
            .OrderByDescending(s => s.LastUsedAtUtc)
            .Select(s => new { s.Id, s.CreatedAtUtc, s.LastUsedAtUtc, s.ExpiresAtUtc, s.UserAgent, current = s.Id == current })
            .ToListAsync(ct);
        return Ok(sessions);
    }

    /// <summary>Signs out every other session of this administrator.</summary>
    [HttpPost("sessions/revoke-others")]
    [AdminAuthenticated]
    public async Task<IActionResult> RevokeOtherSessions(CancellationToken ct)
    {
        var userId = User.UserId();
        var current = User.SessionId();
        var now = DateTime.UtcNow;
        var others = await db.AdminSessions.Where(s => s.UserId == userId && s.Id != current && s.RevokedAtUtc == null).ToListAsync(ct);
        foreach (var s in others)
        {
            s.RevokedAtUtc = now;
            securityState.InvalidateSession(s.Id);
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("admin.sessions.revoke_others", resourceType: "User", resourceId: userId.ToString(),
            summary: $"{others.Count} session(s) revoked", cancellationToken: ct);
        return Ok(new { revoked = others.Count });
    }

    /// <summary>Replaces the recovery codes (shown once). Requires a recent second factor.</summary>
    [HttpPost("mfa/recovery-codes")]
    [AdminAuthenticated]
    [RequireRecentMfa]
    public async Task<IActionResult> RegenerateRecoveryCodes(CancellationToken ct) =>
        Ok(new { recoveryCodes = await auth.RegenerateRecoveryCodesAsync(User.UserId(), ct) });

    private IActionResult SessionResponse(AdminSessionResult session)
    {
        var options = CookieOptions();
        options.Expires = session.SessionExpiresAtUtc;
        Response.Cookies.Append(RefreshCookie, session.RefreshToken, options);
        return Ok(new
        {
            accessToken = session.AccessToken,
            accessTokenExpiresAtUtc = session.AccessTokenExpiresAtUtc,
            sessionExpiresAtUtc = session.SessionExpiresAtUtc,
            profile = session.Profile,
            recoveryCodes = session.RecoveryCodes,
        });
    }

    private CookieOptions CookieOptions() => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps || webAuth.RequireSecureCookie,
        SameSite = SameSiteMode.Strict,
        Path = "/api/admin/auth",
        IsEssential = true,
    };

    // A browser form can't add a custom header, and a cross-origin script needs CORS approval.
    private bool TrustedSessionRequest() => Request.Headers[CsrfHeader] == "1" && TrustedOrigin();

    private bool TrustedOrigin()
    {
        var origin = Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) ||
               webAuth.AllowedOrigins.Contains(origin, StringComparer.Ordinal) ||
               origin == $"{Request.Scheme}://{Request.Host}";
    }

    private ObjectResult Problem(string detail, int statusCode, string? extensions = null)
    {
        var problem = new ProblemDetails { Status = statusCode, Detail = detail, Title = detail };
        if (extensions is not null) problem.Extensions["code"] = extensions;
        return new ObjectResult(problem) { StatusCode = statusCode };
    }
}
