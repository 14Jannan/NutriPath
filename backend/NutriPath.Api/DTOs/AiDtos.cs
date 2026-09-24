namespace NutriPath.Api.DTOs;

/// <param name="Question">The user's question.</param>
/// <param name="LocalDate">The user's local date, so "today" matches their day. Defaults to today in UTC.</param>
public record ChatRequest(string Question, DateOnly? LocalDate = null);
public record ChatResponse(string Answer, List<string> SourcesUsed);
public record ChatHistoryMessage(Guid Id, string Role, string Content, DateTime CreatedAtUtc);
