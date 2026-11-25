using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Models.Admin;

namespace NutriPath.Api.Services.Admin;

public sealed record AdminLoginResult(string Step, string MfaTicket, string? EnrollmentSecret = null, string? OtpAuthUri = null);

public sealed record AdminSessionResult(string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken,
    DateTime SessionExpiresAtUtc, AdminProfile Profile, IReadOnlyList<string>? RecoveryCodes = null);

public sealed record AdminProfile(Guid Id, string Email, string FullName, IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions, bool MfaEnabled);

/// <summary>An expected failure with a message safe to show (no account enumeration).</summary>
public sealed class AdminAuthException(string message, int status = StatusCodes.Status401Unauthorized, string? code = null)
    : Exception(message)
{
    public int Status { get; } = status;
    public string? Code { get; } = code;
}

/// <summary>
/// Admin sign-in: password → (TOTP enrolment on first sign-in | TOTP or recovery code) → session.
/// No privileged token exists until the second factor succeeds. Accounts lock for 15 minutes
/// after 5 failed passwords. Sessions are rows in AdminSessions with rotating refresh tokens.
/// </summary>
public sealed class AdminAuthService(
    NutriPathDbContext db,
    IPasswordHasher passwords,
    AdminTokenService tokens,
    ISecretProtector secrets,
    SecurityState securityState,
    AdminAudit audit,
    TimeProvider clock)
{
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan Lockout = TimeSpan.FromMinutes(15);
    private static readonly Lazy<string> DummyHash = new(() => BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()));
    private const string InvalidCredentials ="Invalid email or password, or this account cannot use the admin portal.";

    public async Task<AdminLoginResult> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        if (!secrets.Configured)
            throw new AdminAuthException("The admin portal is not configured on this server.", StatusCodes.Status503ServiceUnavailable);
        var now = clock.GetUtcNow().UtcDateTime;
        email = email.Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            passwords.Verify(password, DummyHash.Value); // similar timing for unknown accounts
            await audit.WriteAsync("admin.login", "Failure", "User", summary: "Unknown account", actorEmail: email, cancellationToken: ct);
            throw new AdminAuthException(InvalidCredentials);
        }
        if (user.LockoutEndUtc > now)
        {
            await audit.WriteAsync("admin.login", "Denied", "User", user.Id.ToString(), "Account locked", actorUserId: user.Id, actorEmail: user.Email, cancellationToken: ct);
            throw new AdminAuthException("Too many failed attempts. Try again later.", StatusCodes.Status423Locked);
        }
        var roles = await RolesAsync(user.Id, ct);
        if (!passwords.Verify(password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedLogins)
            {
                user.LockoutEndUtc = now.Add(Lockout);
                user.FailedLoginCount = 0;
            }
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync("admin.login", "Failure", "User", user.Id.ToString(), "Wrong password", actorUserId: user.Id, actorEmail: user.Email, cancellationToken: ct);
            throw new AdminAuthException(InvalidCredentials);
        }
        if (roles.Count == 0 || user.IsSuspended || !user.EmailVerified)
        {
            await audit.WriteAsync("admin.login", "Denied", "User", user.Id.ToString(),
                roles.Count == 0 ? "Not an administrator" : user.IsSuspended ? "Suspended" : "Email not verified",
                actorUserId: user.Id, actorEmail: user.Email, cancellationToken: ct);
            throw new AdminAuthException(InvalidCredentials);
        }
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;

        var mfa = await db.AdminMfa.FirstOrDefaultAsync(m => m.UserId == user.Id, ct);
        if (mfa is { Enabled: true })
        {
            await db.SaveChangesAsync(ct);
            return new AdminLoginResult("mfa", tokens.CreateMfaTicket(user, "mfa"));
        }

        // First sign-in: a new secret each time until enrolment completes.
        var secret = Totp.NewSecret();
        var packed = AesGcmSecretProtector.Pack(secrets.Protect(secret, MfaAad(user.Id)));
        if (mfa is null) db.AdminMfa.Add(new AdminMfa { UserId = user.Id, SecretCiphertext = packed });
        else mfa.SecretCiphertext = packed;
        await db.SaveChangesAsync(ct);
        return new AdminLoginResult("mfa-enroll", tokens.CreateMfaTicket(user, "enroll"), secret,
            Totp.OtpAuthUri("NutriPath Admin", user.Email, secret));
    }

    /// <summary>Completes sign-in with a TOTP code (or, when enrolled, a one-time recovery code).</summary>
    public async Task<AdminSessionResult> VerifyMfaAsync(string ticket, string? code, string? recoveryCode,
        string? userAgent, CancellationToken ct = default)
    {
        var principal = tokens.ReadMfaTicket(ticket) ?? throw new AdminAuthException("Your sign-in expired. Please start again.");
        var user = await TicketUserAsync(principal, ct);
        var purpose = principal.FindFirstValue(AdminTokenService.PurposeClaim);
        var mfa = await db.AdminMfa.FirstOrDefaultAsync(m => m.UserId == user.Id, ct)
            ?? throw new AdminAuthException("Your sign-in expired. Please start again.");

        IReadOnlyList<string>? newRecoveryCodes = null;
        if (purpose == "enroll")
        {
            if (mfa.Enabled) throw new AdminAuthException("MFA is already set up. Sign in again.");
            if (!await AcceptTotpAsync(user, mfa, code, ct)) throw await MfaFailure(user, "enrolment", ct);
            mfa.Enabled = true;
            mfa.EnabledAtUtc = clock.GetUtcNow().UtcDateTime;
            newRecoveryCodes = await ReplaceRecoveryCodesAsync(user.Id, ct);
            await audit.WriteAsync("admin.mfa.enrolled", resourceType: "User", resourceId: user.Id.ToString(), actorUserId: user.Id, actorEmail: user.Email, cancellationToken: ct);
        }
        else if (purpose == "mfa" && mfa.Enabled)
        {
            var ok = !string.IsNullOrWhiteSpace(recoveryCode)
                ? await UseRecoveryCodeAsync(user.Id, recoveryCode, ct)
                : await AcceptTotpAsync(user, mfa, code, ct);
            if (!ok) throw await MfaFailure(user, "verification", ct);
        }
        else throw new AdminAuthException("Your sign-in expired. Please start again.");

        user.LastLoginAtUtc = clock.GetUtcNow().UtcDateTime;
        var result = await CreateSessionAsync(user, userAgent, ct);
        await audit.WriteAsync("admin.login", resourceType: "User", resourceId: user.Id.ToString(),
            summary: string.IsNullOrWhiteSpace(recoveryCode) ? "Signed in with TOTP" : "Signed in with a recovery code",
            actorUserId: user.Id, actorEmail: user.Email, cancellationToken: ct);
        return result with { RecoveryCodes = newRecoveryCodes };
    }

    public async Task<AdminSessionResult> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = Hash(refreshToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var session = await db.AdminSessions.FirstOrDefaultAsync(s => s.RefreshTokenHash == hash, ct);
        if (session is null || session.RevokedAtUtc is not null || session.ExpiresAtUtc <= now)
            throw new AdminAuthException("Your session has ended. Please sign in again.", code: "session_expired");
        var user = await db.Users.SingleAsync(u => u.Id == session.UserId, ct);
        var roles = await RolesAsync(user.Id, ct);
        if (user.IsSuspended || roles.Count == 0)
        {
            session.RevokedAtUtc = now;
            await db.SaveChangesAsync(ct);
            throw new AdminAuthException("Your session has ended. Please sign in again.", code: "session_expired");
        }
        var next = NewRefreshToken();
        session.RefreshTokenHash = Hash(next);
        session.LastUsedAtUtc = now;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new AdminAuthException("Your session has ended. Please sign in again.", code: "session_expired");
        }
        var (access, expires) = tokens.CreateAccessToken(user, roles, session);
        return new AdminSessionResult(access, expires, next, session.ExpiresAtUtc, await ProfileAsync(user, roles, ct));
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var hash = Hash(refreshToken);
        var session = await db.AdminSessions.FirstOrDefaultAsync(s => s.RefreshTokenHash == hash, ct);
        if (session is null || session.RevokedAtUtc is not null) return;
        session.RevokedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        securityState.InvalidateSession(session.Id);
        await audit.WriteAsync("admin.logout", resourceType: "AdminSession", resourceId: session.Id.ToString(), actorUserId: session.UserId, cancellationToken: ct);
    }

    /// <summary>Password + current TOTP again, so sensitive actions (keys, roles) can proceed for 15 minutes.</summary>
    public async Task<(string AccessToken, DateTime ExpiresAtUtc)> ReauthenticateAsync(Guid userId, Guid sessionId,
        string password, string code, CancellationToken ct = default)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        var session = await db.AdminSessions.SingleAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        var mfa = await db.AdminMfa.SingleAsync(m => m.UserId == userId, ct);
        if (!passwords.Verify(password, user.PasswordHash) || !await AcceptTotpAsync(user, mfa, code, ct))
        {
            await audit.WriteAsync("admin.reauth", "Failure", "User", userId.ToString(), cancellationToken: ct);
            throw new AdminAuthException("Password or code is incorrect.");
        }
        session.MfaVerifiedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("admin.reauth", resourceType: "User", resourceId: userId.ToString(), cancellationToken: ct);
        return tokens.CreateAccessToken(user, await RolesAsync(userId, ct), session);
    }

    public async Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(Guid userId, CancellationToken ct = default)
    {
        var codes = await ReplaceRecoveryCodesAsync(userId, ct);
        await audit.WriteAsync("admin.mfa.recovery_codes_regenerated", resourceType: "User", resourceId: userId.ToString(), cancellationToken: ct);
        return codes;
    }

    public async Task<AdminProfile> ProfileAsync(User user, IReadOnlyCollection<string>? roles = null, CancellationToken ct = default)
    {
        roles ??= await RolesAsync(user.Id, ct);
        var fullName = await db.UserProfiles.Where(p => p.UserId == user.Id).Select(p => p.FullName).FirstOrDefaultAsync(ct) ?? "";
        var mfa = await db.AdminMfa.AnyAsync(m => m.UserId == user.Id && m.Enabled, ct);
        return new AdminProfile(user.Id, user.Email, fullName, roles.Order().ToList(), Permissions.For(roles).Order().ToList(), mfa);
    }

    public async Task<List<string>> RolesAsync(Guid userId, CancellationToken ct = default) =>
        await db.UserRoles.Where(r => r.UserId == userId).Select(r => r.Role).ToListAsync(ct);

    public static string Hash(string token) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string NewRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    public static string MfaAad(Guid userId) => $"admin-mfa:{userId:N}";

    private async Task<AdminSessionResult> CreateSessionAsync(User user, string? userAgent, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var refresh = NewRefreshToken();
        var session = new AdminSession
        {
            UserId = user.Id,
            RefreshTokenHash = Hash(refresh),
            CreatedAtUtc = now,
            LastUsedAtUtc = now,
            MfaVerifiedAtUtc = now,
            ExpiresAtUtc = now.Add(AdminTokenService.SessionLifetime),
            UserAgent = userAgent is { Length: > 200 } ? userAgent[..200] : userAgent,
        };
        db.AdminSessions.Add(session);
        await db.SaveChangesAsync(ct);
        var roles = await RolesAsync(user.Id, ct);
        var (access, expires) = tokens.CreateAccessToken(user, roles, session);
        return new AdminSessionResult(access, expires, refresh, session.ExpiresAtUtc, await ProfileAsync(user, roles, ct));
    }

    private async Task<User> TicketUserAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId))
            throw new AdminAuthException("Your sign-in expired. Please start again.");
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || user.IsSuspended || user.SecurityStamp != principal.FindFirstValue(AdminTokenService.StampClaim) ||
            (await RolesAsync(user.Id, ct)).Count == 0)
            throw new AdminAuthException("Your sign-in expired. Please start again.");
        return user;
    }

    private async Task<bool> AcceptTotpAsync(User user, AdminMfa mfa, string? code, CancellationToken ct)
    {
        var secret = secrets.Unprotect(AesGcmSecretProtector.Unpack(mfa.SecretCiphertext), MfaAad(user.Id));
        var step = Totp.Verify(secret, code, clock.GetUtcNow(), mfa.LastUsedTimeStep);
        if (step is null) return false;
        mfa.LastUsedTimeStep = step.Value;
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false; // the same code was accepted concurrently
        }
    }

    private async Task<AdminAuthException> MfaFailure(User user, string stage, CancellationToken ct)
    {
        user.FailedLoginCount++;
        if (user.FailedLoginCount >= MaxFailedLogins)
        {
            user.LockoutEndUtc = clock.GetUtcNow().UtcDateTime.Add(Lockout);
            user.FailedLoginCount = 0;
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("admin.mfa", "Failure", "User", user.Id.ToString(), $"Invalid code during {stage}", actorUserId: user.Id, actorEmail: user.Email, cancellationToken: ct);
        return new AdminAuthException("That code is not valid. Check your authenticator app and try again.");
    }

    private async Task<IReadOnlyList<string>> ReplaceRecoveryCodesAsync(Guid userId, CancellationToken ct)
    {
        await db.AdminRecoveryCodes.Where(c => c.UserId == userId).ExecuteDeleteAsyncSafe(db, ct);
        var codes = Enumerable.Range(0, 10).Select(_ => RecoveryCode()).ToList();
        db.AdminRecoveryCodes.AddRange(codes.Select(c => new AdminRecoveryCode { UserId = userId, CodeHash = Hash(Normalize(c)) }));
        await db.SaveChangesAsync(ct);
        return codes;
    }

    private async Task<bool> UseRecoveryCodeAsync(Guid userId, string code, CancellationToken ct)
    {
        var hash = Hash(Normalize(code));
        var row = await db.AdminRecoveryCodes.FirstOrDefaultAsync(c => c.UserId == userId && c.CodeHash == hash && c.UsedAtUtc == null, ct);
        if (row is null) return false;
        row.UsedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static string RecoveryCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = Enumerable.Range(0, 10).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray();
        return $"{new string(chars, 0, 5)}-{new string(chars, 5, 5)}";
    }

    private static string Normalize(string code) => code.Replace("-", "").Replace(" ", "").ToUpperInvariant();
}

internal static class QueryableDeleteExtensions
{
    /// <summary>ExecuteDelete on relational providers; load-and-remove on the in-memory test provider.</summary>
    public static async Task ExecuteDeleteAsyncSafe<T>(this IQueryable<T> query, NutriPathDbContext db, CancellationToken ct)
        where T : class
    {
        if (db.Database.IsRelational()) await query.ExecuteDeleteAsync(ct);
        else db.RemoveRange(await query.ToListAsync(ct));
    }
}
