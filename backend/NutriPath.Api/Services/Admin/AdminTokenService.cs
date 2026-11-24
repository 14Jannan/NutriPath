using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NutriPath.Api.Models;
using NutriPath.Api.Models.Admin;

namespace NutriPath.Api.Services.Admin;

/// <summary>
/// Tokens for the admin portal. They use their own audiences, so a consumer token can never
/// reach an admin endpoint (and an admin token can't be used by the consumer API):
///   NutriPath.Admin     — access token, issued only after MFA, 10 minutes
///   NutriPath.AdminMfa  — 5-minute ticket between password and MFA steps; grants nothing else
/// </summary>
public sealed class AdminTokenService(IOptions<JwtSettings> jwt)
{
    public const string AdminAudience = "NutriPath.Admin";
    public const string MfaAudience = "NutriPath.AdminMfa";
    public const string AdminScheme = "AdminBearer";
    public const string MfaScheme = "AdminMfa";
    public const string StampClaim = "sv";
    public const string SessionClaim = "sid";
    public const string MfaAtClaim = "mfa_at";
    public const string PermissionClaim = "perm";
    public const string PurposeClaim = "purpose";

    public static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MfaTicketLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
    public static readonly TimeSpan RecentMfa = TimeSpan.FromMinutes(15);

    public (string Token, DateTime ExpiresAtUtc) CreateAccessToken(User user, IReadOnlyCollection<string> roles,
        AdminSession session)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(StampClaim, user.SecurityStamp),
            new(SessionClaim, session.Id.ToString()),
            new(MfaAtClaim, new DateTimeOffset(session.MfaVerifiedAtUtc).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(Permissions.For(roles).Order().Select(p => new Claim(PermissionClaim, p)));
        return Write(claims, AdminAudience, AccessLifetime);
    }

    public string CreateMfaTicket(User user, string purpose) =>
        Write([
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(StampClaim, user.SecurityStamp),
            new(PurposeClaim, purpose),
        ], MfaAudience, MfaTicketLifetime).Token;

    public ClaimsPrincipal? ReadMfaTicket(string ticket)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        try
        {
            return handler.ValidateToken(ticket, Parameters(jwt.Value, MfaAudience), out _);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }

    public static TokenValidationParameters Parameters(JwtSettings settings, string audience) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = settings.Issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateIssuerSigningKey = true,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
    };

    private (string Token, DateTime ExpiresAtUtc) Write(IEnumerable<Claim> claims, string audience, TimeSpan lifetime)
    {
        var settings = jwt.Value;
        var expires = DateTime.UtcNow.Add(lifetime);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(settings.Issuer, audience, claims, expires: expires, signingCredentials: credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
