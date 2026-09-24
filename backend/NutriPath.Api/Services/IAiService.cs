using NutriPath.Api.DTOs;

namespace NutriPath.Api.Services;

public interface IAiService
{
    Task<ChatResponse> ChatAsync(Guid userId, string question, DateOnly today);
    Task<List<ChatHistoryMessage>> GetHistoryAsync(Guid userId);
    Task StartNewConversationAsync(Guid userId);
}
