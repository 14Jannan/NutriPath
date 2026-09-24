using NutriPath.Api.DTOs;

namespace NutriPath.Api.Services;

public interface IAiService
{
    Task<ChatResponse> ChatAsync(Guid userId, string question);
    Task<List<ChatHistoryMessage>> GetHistoryAsync(Guid userId);
}
