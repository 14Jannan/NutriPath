namespace NutriPath.Api.Models;

/// <summary>
/// Login identity only. Kept deliberately minimal — everything about
/// who the person actually is (goals, allergies, body stats) lives in
/// UserProfile, one-to-one with this table.
/// </summary>

public class User
{
    public Guid Id {get; set;}=Guid.NewGuid();
    public string Email {get; set;}=string.Empty;
    public string PasswordHash {get; set;}=string.Empty;
    public DateTime CreatedAtUtc {get; set;}=DateTime.UtcNow;
    public UserProfile? Profile {get; set;}
    public bool EmailVerified { get; set; } = false;

    public string? EmailVerificationCode { get; set; }
    public DateTime? EmailVerificationCodeExpiresAtUtc { get; set; }

    public string? PasswordResetCode { get; set; }
    public DateTime? PasswordResetCodeExpiresAtUtc { get; set; }

    // Storing a HASH of the refresh token, never the token itself — same
    // principle as passwords: if the database were ever exposed, the
    // attacker still can't use these values directly.
    public string? RefreshTokenHash { get; set; }
    public DateTime? RefreshTokenExpiresAtUtc { get; set; }

    // Account controls managed from the admin portal.
    public bool IsSuspended { get; set; }
    public DateTime? SuspendedAtUtc { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(300)]
    public string? SuspensionReason { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }

    // Changes whenever sessions must end (suspension, role change, revocation). Every access
    // token carries it, and tokens with an old stamp are rejected.
    [System.ComponentModel.DataAnnotations.MaxLength(40)]
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
}
