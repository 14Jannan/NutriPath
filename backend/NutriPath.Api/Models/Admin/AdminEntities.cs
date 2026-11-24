using System.ComponentModel.DataAnnotations;

namespace NutriPath.Api.Models.Admin;

/// <summary>An administrative role held by a user. Consumers have no rows here.</summary>
public sealed class UserRole
{
    public Guid UserId { get; set; }
    [MaxLength(40)] public string Role { get; set; } = "";
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? AssignedByUserId { get; set; }
}

/// <summary>One signed-in admin browser session. Only a hash of the refresh token is stored.</summary>
public sealed class AdminSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    [MaxLength(100)] public string RefreshTokenHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    [MaxLength(200)] public string? UserAgent { get; set; }
    // Last time this session proved a second factor; sensitive actions require it to be recent.
    public DateTime MfaVerifiedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>TOTP enrolment. The secret is AES-256-GCM encrypted with the credential key.</summary>
public sealed class AdminMfa
{
    [Key] public Guid UserId { get; set; }
    [MaxLength(400)] public string SecretCiphertext { get; set; } = "";
    public bool Enabled { get; set; }
    public DateTime? EnabledAtUtc { get; set; }
    // Highest accepted 30-second step, so a code can't be replayed.
    public long LastUsedTimeStep { get; set; }
}

public sealed class AdminRecoveryCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    [MaxLength(100)] public string CodeHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAtUtc { get; set; }
}

public sealed class AdminInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(256)] public string Email { get; set; } = "";
    public List<string> Roles { get; set; } = [];
    [MaxLength(100)] public string TokenHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}

/// <summary>Append-only record of an administrative action. Never holds secrets or health data.</summary>
public sealed class AdminAuditLog
{
    public long Id { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public Guid? ActorUserId { get; set; }
    [MaxLength(256)] public string? ActorEmail { get; set; }
    [MaxLength(200)] public string? ActorRoles { get; set; }
    [MaxLength(80)] public string Action { get; set; } = "";
    [MaxLength(60)] public string? ResourceType { get; set; }
    [MaxLength(100)] public string? ResourceId { get; set; }
    [MaxLength(20)] public string Outcome { get; set; } = "Success";
    [MaxLength(64)] public string? CorrelationId { get; set; }
    [MaxLength(1000)] public string? Summary { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
}

public static class CredentialStatus
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public const string Revoked = "Revoked";
}

/// <summary>An encrypted external API credential version (Gemini, Groq, USDA).</summary>
public sealed class ProviderCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(40)] public string Provider { get; set; } = "";
    public int Version { get; set; }
    public byte[] Ciphertext { get; set; } = [];
    public byte[] Nonce { get; set; } = [];
    public byte[] Tag { get; set; } = [];
    public int KeyVersion { get; set; }
    // Last four characters only, for recognising which key is active.
    [MaxLength(12)] public string Hint { get; set; } = "";
    [MaxLength(20)] public string Status { get; set; } = CredentialStatus.Inactive;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? LastValidatedAtUtc { get; set; }
}

/// <summary>A versioned, validated JSON settings document (e.g. "ai"). History is kept in RuntimeSettingVersions.</summary>
public sealed class RuntimeSetting
{
    [Key, MaxLength(60)] public string Key { get; set; } = "";
    public string Json { get; set; } = "{}";
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedByUserId { get; set; }
}

public sealed class RuntimeSettingVersion
{
    public long Id { get; set; }
    [MaxLength(60)] public string Key { get; set; } = "";
    public int Version { get; set; }
    public string Json { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
    [MaxLength(300)] public string? Note { get; set; }
}

/// <summary>Operational metadata for one assistant request. No prompt or answer text.</summary>
public sealed class AiRequestMetric
{
    public long Id { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public Guid? UserId { get; set; }
    [MaxLength(20)] public string Provider { get; set; } = "";
    [MaxLength(60)] public string Model { get; set; } = "";
    [MaxLength(8)] public string Language { get; set; } = "en";
    public int TotalTokens { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int LatencyMs { get; set; }
    public int RetrievalMs { get; set; }
    // Success, ProviderError, RateLimited, NotPermitted
    [MaxLength(20)] public string Outcome { get; set; } = "Success";
    public int? HttpStatus { get; set; }
    public bool ValidationPassed { get; set; }
    public int Citations { get; set; }
    public bool InsufficientEvidence { get; set; }
    public int ConfigVersion { get; set; }
}

/// <summary>Per-user override of the assistant token allowance.</summary>
public sealed class UserAiLimit
{
    [Key] public Guid UserId { get; set; }
    public int TokenLimit { get; set; }
    public int WindowHours { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedByUserId { get; set; }
    [MaxLength(300)] public string? Reason { get; set; }
}

/// <summary>Durable background work (reindexing, USDA sync). Processed by BackgroundJobWorker.</summary>
public sealed class BackgroundJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(60)] public string Type { get; set; } = "";
    // Queued, Running, Succeeded, Failed, Cancelled
    [MaxLength(20)] public string Status { get; set; } = "Queued";
    public string PayloadJson { get; set; } = "{}";
    public int Progress { get; set; }
    public int Attempts { get; set; }
    [MaxLength(500)] public string? Error { get; set; }
    [MaxLength(1000)] public string? ResultJson { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public bool CancelRequested { get; set; }
}
