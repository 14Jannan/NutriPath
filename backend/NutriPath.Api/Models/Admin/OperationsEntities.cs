using System.ComponentModel.DataAnnotations;

namespace NutriPath.Api.Models.Admin;

/// <summary>
/// A user's rating of one assistant answer. Submitting it shares that question and answer with
/// reviewers, so a copy is kept here, encrypted with the chat-message key.
/// </summary>
public sealed class AnswerFeedback
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid MessageId { get; set; }
    public bool Helpful { get; set; }
    // Incorrect, Unsafe, WrongLanguage, NotRelevant, MissingSources, Other (null when helpful).
    [MaxLength(30)] public string? Reason { get; set; }
    // Encrypted free text, question and answer.
    public string? CommentCiphertext { get; set; }
    public string QuestionCiphertext { get; set; } = "";
    public string AnswerCiphertext { get; set; } = "";
    [MaxLength(5)] public string Language { get; set; } = "en";
    [MaxLength(20)] public string? Provider { get; set; }
    // New, Reviewed, Actioned, Dismissed.
    [MaxLength(20)] public string Status { get; set; } = "New";
    [MaxLength(500)] public string? ReviewNote { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A possible safety problem with the assistant, raised automatically or by a reviewer.</summary>
public sealed class SafetyIncident
{
    public Guid Id { get; set; } = Guid.NewGuid();
    // AutomaticValidation, UserReport, Reviewer.
    [MaxLength(30)] public string Source { get; set; } = "Reviewer";
    // AllergySafety, MedicalAdvice, Hallucination, HarmfulAdvice, Privacy, Other.
    [MaxLength(30)] public string Category { get; set; } = "Other";
    // Low, Medium, High, Critical.
    [MaxLength(10)] public string Severity { get; set; } = "Medium";
    // Open, Investigating, Resolved, Dismissed.
    [MaxLength(20)] public string Status { get; set; } = "Open";
    [MaxLength(300)] public string Title { get; set; } = "";
    // Never contains health data; details live in the linked feedback, which is encrypted.
    [MaxLength(2000)] public string? Description { get; set; }
    [MaxLength(2000)] public string? Resolution { get; set; }
    public Guid? FeedbackId { get; set; }
    [MaxLength(5)] public string? Language { get; set; }
    [MaxLength(20)] public string? Provider { get; set; }
    [MaxLength(60)] public string? Model { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}

/// <summary>An in-portal notification, visible to administrators holding <see cref="Permission"/>.</summary>
public sealed class AdminNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(40)] public string Type { get; set; } = "";
    [MaxLength(200)] public string Title { get; set; } = "";
    [MaxLength(500)] public string? Body { get; set; }
    [MaxLength(200)] public string? Link { get; set; }
    [MaxLength(60)] public string Permission { get; set; } = "";
    // info, warning, danger.
    [MaxLength(10)] public string Tone { get; set; } = "info";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AdminNotificationRead
{
    public Guid NotificationId { get; set; }
    public Guid UserId { get; set; }
    public DateTime ReadAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A Tamil, Sinhala or romanised nutrition term mapped to English search terms. Approved terms
/// extend the built-in lexicon used for keyword search; a reviewer other than the author approves.
/// </summary>
public sealed class NutritionTerm
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(5)] public string Language { get; set; } = "ta";
    // Native-script prefixes and romanised spellings, newline-separated.
    [MaxLength(1000)] public string NativeTerms { get; set; } = "";
    [MaxLength(1000)] public string RomanisedTerms { get; set; } = "";
    [MaxLength(200)] public string EnglishTerms { get; set; } = "";
    [MaxLength(300)] public string? Notes { get; set; }
    // Draft, Approved, Archived.
    [MaxLength(20)] public string Status { get; set; } = "Draft";
    public Guid? CreatedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAtUtc { get; set; }
}

/// <summary>A retrieval benchmark run over the repository's evaluation question sets.</summary>
public sealed class EvaluationRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(30)] public string Dataset { get; set; } = "nutrition";
    [MaxLength(20)] public string Mode { get; set; } = "hybrid";
    public int TopK { get; set; } = 5;
    [MaxLength(80)] public string? EmbeddingModel { get; set; }
    // Queued, Running, Succeeded, Failed.
    [MaxLength(20)] public string Status { get; set; } = "Queued";
    public Guid? JobId { get; set; }
    public string? MetricsJson { get; set; }
    public string? ResultsJson { get; set; }
    [MaxLength(300)] public string? Error { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAtUtc { get; set; }
}

/// <summary>
/// A user's request to export or delete their data. Deletion removes the account but keeps this
/// row (without personal data) as the record that the request was honoured.
/// </summary>
public sealed class DataRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    // Export, Deletion.
    [MaxLength(20)] public string Type { get; set; } = "Export";
    // Pending, Completed, Rejected, Cancelled.
    [MaxLength(20)] public string Status { get; set; } = "Pending";
    // Cleared once the account is deleted.
    [MaxLength(256)] public string? Email { get; set; }
    [MaxLength(500)] public string? UserNote { get; set; }
    [MaxLength(500)] public string? AdminNote { get; set; }
    // The export, encrypted with the chat-message key; removed when it expires.
    public string? ExportCiphertext { get; set; }
    public DateTime? ExportExpiresAtUtc { get; set; }
    public Guid? ProcessedByUserId { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
