using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NutriPath.Api.DTOs;
using NutriPath.Api.Models;
using NutriPath.Api.Services;

namespace NutriPath.Api.Controllers;

/// <summary>Registration, email verification, login and password reset. Rate limited to 5 requests per minute per IP (except refresh).</summary>
[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting(RateLimitPolicies.Auth)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AuthController(
    IAuthService authService,
    WebAuthSettings webAuth,
    IOptions<JwtSettings> jwtSettings) : ControllerBase
{
    internal const string WebHeader = "X-NutriPath-Web";
    private const string RefreshCookie = "nutripath_refresh";

    private bool IsWeb => Request.Headers.ContainsKey(WebHeader);

    private bool IsTrustedSessionRequest()
    {
        if (!IsWeb)
            return !Request.Headers.ContainsKey("Origin")
                && !Request.Headers.ContainsKey("Sec-Fetch-Site")
                && !Request.Cookies.ContainsKey(RefreshCookie);

        // A browser form cannot set this header. Cross-origin scripts need a
        // successful CORS preflight, and the POST itself must have a trusted Origin.
        return Request.Headers[WebHeader] == "1"
            && webAuth.AllowedOrigins.Contains(Request.Headers.Origin.ToString(), StringComparer.Ordinal);
    }

    private CookieOptions RefreshCookieOptions() => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps || webAuth.RequireSecureCookie,
        SameSite = webAuth.CrossSiteCookie ? SameSiteMode.None : SameSiteMode.Lax,
        Path = "/api/auth",
    };

    private IActionResult SessionResponse(AuthResponse session)
    {
        if (!IsWeb) return Ok(session);

        var options = RefreshCookieOptions();
        options.MaxAge = TimeSpan.FromDays(jwtSettings.Value.RefreshTokenDays);
        Response.Cookies.Append(RefreshCookie, session.RefreshToken, options);
        return Ok(new { session.AccessToken, session.AccessTokenExpiresAtUtc,
            session.UserId, session.Email, session.FullName });
    }

    /// <summary>Creates an account and emails a 6-digit verification code.</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            await authService.RegisterAsync(request);
            return Ok(new { message = "Registered. Check the server console for your verification code." });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>Verifies the email address with the emailed code.</summary>
    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest request)
    {
        try
        {
            await authService.VerifyOtpAsync(request);
            return Ok(new { message = "Email verified. You can now log in." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Signs in a verified user and returns an access token and refresh token.</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!IsTrustedSessionRequest()) return StatusCode(StatusCodes.Status403Forbidden);
        try
        {
            return SessionResponse(await authService.LoginAsync(request));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "The session changed. Please try signing in again." });
        }
    }

    // Refresh tokens are long random values that can't be guessed, and the
    // app calls this automatically, so it's exempt from the auth limit.
    /// <summary>Exchanges a refresh token for a new access/refresh token pair (the old refresh token stops working).</summary>
    [HttpPost("refresh")]
    [DisableRateLimiting]
    public async Task<IActionResult> Refresh([FromBody] SessionRequest request)
    {
        if (!IsTrustedSessionRequest()) return StatusCode(StatusCodes.Status403Forbidden);
        var refreshToken = IsWeb ? Request.Cookies[RefreshCookie] : request.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
            return Unauthorized(new { message = "Invalid refresh token." });

        try
        {
            return SessionResponse(await authService.RefreshAsync(new RefreshRequest(refreshToken)));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    /// <summary>Revokes the presented refresh credential and expires the web session cookie.</summary>
    [HttpPost("logout")]
    [DisableRateLimiting]
    public async Task<IActionResult> Logout([FromBody] SessionRequest request)
    {
        if (!IsTrustedSessionRequest()) return StatusCode(StatusCodes.Status403Forbidden);
        var refreshToken = IsWeb ? Request.Cookies[RefreshCookie] : request.RefreshToken;
        // Native callers prove possession of the same high-entropy credential
        // used for renewal, even after their access token has expired.
        if (!string.IsNullOrWhiteSpace(refreshToken))
            await authService.LogoutAsync(refreshToken);
        if (IsWeb)
            Response.Cookies.Delete(RefreshCookie, RefreshCookieOptions());
        return NoContent();
    }

    /// <summary>Emails a password reset code if the account exists (always returns 200, so emails can't be probed).</summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        await authService.ForgotPasswordAsync(request);
        return Ok(new { message = "If that email exists, a reset code has been sent." });
    }

    /// <summary>Sets a new password using the emailed reset code.</summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        try
        {
            await authService.ResetPasswordAsync(request);
            return Ok(new { message = "Password updated. Please log in again." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "The account changed. Please try resetting the password again." });
        }
    }
}
