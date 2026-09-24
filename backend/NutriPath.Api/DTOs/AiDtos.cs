namespace NutriPath.Api.DTOs;

public record ChatRequest(string Question);
public record ChatResponse(string Answer, List<string> SourcesUsed);
