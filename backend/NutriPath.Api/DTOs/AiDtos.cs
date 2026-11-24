namespace NutriPath.Api.DTOs;

/// <param name="Question">The user's question.</param>
/// <param name="LocalDate">The user's local date, used when LocalDateTime isn't sent. Defaults to today in UTC.</param>
/// <param name="LocalDateTime">The user's local date and time with UTC offset, e.g. 2026-09-25T14:05:00+05:30.</param>
/// <param name="ConversationId">Continue this conversation; omit to continue the most recent one.</param>
/// <param name="Language">Reply language: "en", "ta", "si", or "auto" (default) to detect it from the question.</param>
public record ChatRequest(
    string Question, DateOnly? LocalDate = null, DateTimeOffset? LocalDateTime = null, Guid? ConversationId = null,
    string? Language = null);
public record KnowledgeSource(Guid Id, string Title, string Url);
/// <summary>
/// An assistant answer. Language is the language it was written in ("en", "ta" or "si"),
/// LanguageSource why that language was chosen (explicit, selected, detected, preference,
/// default or disabled), and Provider the AI provider that generated it (Gemini or Groq).
/// </summary>
public record ChatResponse(string Answer, List<string> SourcesUsed, Guid ConversationId, AiUsageStatus? Usage = null,
    List<KnowledgeSource>? References = null, bool InsufficientEvidence = false,
    string Language = "en", string? LanguageSource = null, string? Provider = null);

/// <summary>The user's assistant allowance. ResetsAtUtc is set only while Locked.</summary>
public record AiUsageStatus(int TokensUsed, int TokenLimit, int WindowHours, bool Locked, DateTime? ResetsAtUtc);
public record ChatHistoryMessage(Guid Id, string Role, string Content, DateTime CreatedAtUtc,
    List<KnowledgeSource>? References = null, bool InsufficientEvidence = false);

/// <summary>One past conversation in the history list. Title is its first question.</summary>
public record ConversationSummary(Guid Id, string Title, DateTime StartedAtUtc, DateTime LastMessageAtUtc, int MessageCount);

/// <summary>A message matching a history search, with a short snippet around the match.</summary>
public record ChatSearchResult(
    Guid ConversationId, string ConversationTitle, Guid MessageId, string Role, string Snippet, DateTime CreatedAtUtc);

public record NewConversationResponse(Guid Id);
