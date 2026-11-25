using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Models.Admin;

namespace NutriPath.Api.Services.Admin;

public sealed record FeedbackRequest(bool Helpful, string? Reason, string? Comment);

/// <summary>User feedback on assistant answers and the safety incidents raised from it.</summary>
public sealed class QualityService(NutriPathDbContext db, IMessageProtector protector, AdminNotifications notifications)
{
    public static readonly IReadOnlyList<string> Reasons = ["Incorrect", "Unsafe", "WrongLanguage", "NotRelevant", "MissingSources", "Other"];
    public static readonly IReadOnlyList<string> FeedbackStatuses = ["New", "Reviewed", "Actioned", "Dismissed"];
    public static readonly IReadOnlyList<string> IncidentStatuses = ["Open", "Investigating", "Resolved", "Dismissed"];
    public static readonly IReadOnlyList<string> Categories = ["AllergySafety", "MedicalAdvice", "Hallucination", "HarmfulAdvice", "Privacy", "Other"];
    public static readonly IReadOnlyList<string> Severities = ["Low", "Medium", "High", "Critical"];

    /// <summary>Rates one of the user's own assistant answers. Rating again replaces the earlier rating.</summary>
    public async Task<AnswerFeedback> SubmitAsync(Guid userId, Guid messageId, FeedbackRequest request, CancellationToken ct)
    {
        if (!request.Helpful && (request.Reason is null || !Reasons.Contains(request.Reason)))
            throw new AdminRuleException("Choose what was wrong with the answer.", StatusCodes.Status400BadRequest);
        var comment = request.Comment?.Trim();
        if (comment is { Length: > 1000 }) throw new AdminRuleException("Comments can be at most 1,000 characters.", StatusCodes.Status400BadRequest);

        var answer = await db.AiMessages.AsNoTracking().Include(m => m.Conversation)
            .SingleOrDefaultAsync(m => m.Id == messageId && m.Role == AiMessageRole.Assistant && m.Conversation!.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Answer not found.");
        var question = await db.AiMessages.AsNoTracking()
            .Where(m => m.ConversationId == answer.ConversationId && m.Role == AiMessageRole.User && m.CreatedAtUtc <= answer.CreatedAtUtc)
            .OrderByDescending(m => m.CreatedAtUtc).Select(m => m.Content).FirstOrDefaultAsync(ct);
        var (language, provider) = Metadata(answer.RetrievedContextJson);

        var feedback = await db.AnswerFeedback.SingleOrDefaultAsync(f => f.UserId == userId && f.MessageId == messageId, ct);
        var isNew = feedback is null;
        feedback ??= new AnswerFeedback { UserId = userId, MessageId = messageId };
        feedback.Helpful = request.Helpful;
        feedback.Reason = request.Helpful ? null : request.Reason;
        feedback.CommentCiphertext = string.IsNullOrEmpty(comment) ? null : protector.Protect(comment);
        // The stored content is already encrypted with the same key; it is copied, never decrypted here.
        feedback.QuestionCiphertext = question ?? protector.Protect("");
        feedback.AnswerCiphertext = answer.Content;
        feedback.Language = language;
        feedback.Provider = provider;
        feedback.Status = "New";
        feedback.CreatedAtUtc = DateTime.UtcNow;
        if (isNew) db.AnswerFeedback.Add(feedback);

        if (!request.Helpful && request.Reason == "Unsafe" &&
            !await db.SafetyIncidents.AnyAsync(i => i.FeedbackId == feedback.Id, ct))
        {
            db.SafetyIncidents.Add(new SafetyIncident
            {
                Source = "UserReport", Category = "HarmfulAdvice", Severity = "High", Title = "A user reported an answer as unsafe",
                FeedbackId = feedback.Id, Language = language, Provider = provider,
            });
            notifications.Add("safety.reported", "A user reported an unsafe answer", Permissions.SafetyRead,
                $"Language {language}, provider {provider ?? "unknown"}.", "/safety", "danger");
        }
        await db.SaveChangesAsync(ct);
        return feedback;
    }

    /// <summary>Raised by the assistant when validation blocked an answer that guaranteed allergy safety.</summary>
    public static void RaiseBlockedAllergyGuarantee(NutriPathDbContext db, string language, string provider, string model)
    {
        db.SafetyIncidents.Add(new SafetyIncident
        {
            Source = "AutomaticValidation", Category = "AllergySafety", Severity = "High",
            Title = "Blocked an answer that guaranteed allergy safety",
            Description = "The model's answer claimed a food was safe for an allergy. Validation replaced it with the safe fallback before the user saw it.",
            Language = language, Provider = provider, Model = model.Length > 60 ? model[..60] : model,
        });
        db.AdminNotifications.Add(new AdminNotification
        {
            Type = "safety.blocked", Title = "Validation blocked an allergy-safety guarantee", Permission = Permissions.SafetyRead,
            Body = $"{provider} · {language}. The user received the safe fallback.", Link = "/safety", Tone = "warning",
        });
    }

    public string Decrypt(string? ciphertext) => string.IsNullOrEmpty(ciphertext) ? "" : protector.Unprotect(ciphertext);

    private (string Language, string? Provider) Metadata(string? retrievedContext)
    {
        if (string.IsNullOrEmpty(retrievedContext)) return ("en", null);
        try
        {
            using var json = JsonDocument.Parse(protector.Unprotect(retrievedContext));
            var root = json.RootElement;
            return (root.TryGetProperty("language", out var l) ? l.GetString() ?? "en" : "en",
                root.TryGetProperty("provider", out var p) ? p.GetString() : null);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or System.Security.Cryptography.CryptographicException or InvalidOperationException)
        {
            return ("en", null);
        }
    }
}
