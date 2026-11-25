using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NutriPath.Api.Models.Admin;
using NutriPath.Api.Services.Admin;

namespace NutriPath.Api.Authorization;

/// <summary>
/// Requires an admin access token (admin audience, issued after MFA) that carries the permission.
/// Enforced on the server for every admin endpoint, independent of what the portal UI shows.
/// </summary>
public sealed class AdminPermissionAttribute : AuthorizeAttribute
{
    public AdminPermissionAttribute(string permission)
    {
        Policy = AdminPolicies.For(permission);
        AuthenticationSchemes = AdminTokenService.AdminScheme;
    }
}

/// <summary>Any signed-in administrator (profile, own sessions, sign-out).</summary>
public sealed class AdminAuthenticatedAttribute : AuthorizeAttribute
{
    public AdminAuthenticatedAttribute()
    {
        Policy = AdminPolicies.Authenticated;
        AuthenticationSchemes = AdminTokenService.AdminScheme;
    }
}

public static class AdminPolicies
{
    public const string Authenticated = "admin:authenticated";
    public static string For(string permission) => "perm:" + permission;

    public static void Add(AuthorizationOptions options)
    {
        options.AddPolicy(Authenticated, p => p.AddAuthenticationSchemes(AdminTokenService.AdminScheme)
            .RequireAuthenticatedUser().RequireClaim(AdminTokenService.SessionClaim));
        foreach (var permission in Permissions.All)
            options.AddPolicy(For(permission), p => p.AddAuthenticationSchemes(AdminTokenService.AdminScheme)
                .RequireAuthenticatedUser().RequireClaim(AdminTokenService.PermissionClaim, permission));
    }
}

/// <summary>
/// Sensitive actions (API keys, roles, administrator changes) need a second factor within the
/// last 15 minutes. Otherwise 401 with code "reauth_required"; the portal then asks for the
/// password and a TOTP code (POST /api/admin/auth/reauth) and retries.
/// </summary>
public sealed class RequireRecentMfaAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var claim = context.HttpContext.User.FindFirstValue(AdminTokenService.MfaAtClaim);
        if (!long.TryParse(claim, out var seconds) ||
            DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(seconds) > AdminTokenService.RecentMfa)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Please confirm it's you",
                Detail = "Enter your password and authenticator code to continue.",
                Extensions = { ["code"] = "reauth_required" },
            }) { StatusCode = StatusCodes.Status401Unauthorized };
            return;
        }
        await next();
    }
}

public static class AdminClaims
{
    public static Guid UserId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
    public static Guid SessionId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(AdminTokenService.SessionClaim)!);
    public static IReadOnlyList<string> Roles(this ClaimsPrincipal user) => user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
    public static bool Has(this ClaimsPrincipal user, string permission) =>
        user.HasClaim(AdminTokenService.PermissionClaim, permission);
}
