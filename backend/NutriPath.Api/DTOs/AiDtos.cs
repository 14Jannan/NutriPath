namespace NutriPath.Api.DTOs;

public record ChatRequest(string Question);
public record ChatResponse(string Answer, List<string> SourcesUsed);
public record ChatHistoryMessage(Guid Id, string Role, string Content, DateTime CreatedAtUtc);
