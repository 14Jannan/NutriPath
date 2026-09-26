namespace NutriPath.Api.DTOs;

/// <param name="Question">The user's question.</param>
/// <param name="LocalDate">The user's local date, used when LocalDateTime isn't sent. Defaults to today in UTC.</param>
/// <param name="LocalDateTime">The user's local date and time with UTC offset, e.g. 2026-09-25T14:05:00+05:30.</param>
/// <param name="ConversationId">Continue this conversation; omit to continue the most recent one.</param>
public record ChatRequest(
    string Question, DateOnly? LocalDate = null, DateTimeOffset? LocalDateTime = null, Guid? ConversationId = null);
public record ChatResponse(string Answer, List<string> SourcesUsed, Guid ConversationId, AiUsageStatus? Usage = null);

/// <summary>The user's assistant allowance. ResetsAtUtc is set only while Locked.</summary>
public record AiUsageStatus(int TokensUsed, int TokenLimit, int WindowHours, bool Locked, DateTime? ResetsAtUtc);
public record ChatHistoryMessage(Guid Id, string Role, string Content, DateTime CreatedAtUtc);

/// <summary>One past conversation in the history list. Title is its first question.</summary>
public record ConversationSummary(Guid Id, string Title, DateTime StartedAtUtc, DateTime LastMessageAtUtc, int MessageCount);

/// <summary>A message matching a history search, with a short snippet around the match.</summary>
public record ChatSearchResult(
    Guid ConversationId, string ConversationTitle, Guid MessageId, string Role, string Snippet, DateTime CreatedAtUtc);

public record NewConversationResponse(Guid Id);
