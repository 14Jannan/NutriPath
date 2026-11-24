using NutriPath.Api.DTOs;

namespace NutriPath.Api.Services;

public interface IAiService
{
    /// <summary>Answers a question. Throws KeyNotFoundException if conversationId isn't the user's.</summary>
    /// <remarks>language is "en", "ta", "si", or null/"auto" to resolve it from the question.</remarks>
    Task<ChatResponse> ChatAsync(Guid userId, string question, ClientClock clock, Guid? conversationId = null,
        string? language = null, CancellationToken cancellationToken = default);
    Task<List<ChatHistoryMessage>> GetHistoryAsync(Guid userId);
    Task<Guid> StartNewConversationAsync(Guid userId);
    Task<List<ConversationSummary>> ListConversationsAsync(Guid userId);
    /// <summary>Throws KeyNotFoundException if the conversation isn't the user's.</summary>
    Task<List<ChatHistoryMessage>> GetConversationMessagesAsync(Guid userId, Guid conversationId);
    Task<List<ChatSearchResult>> SearchAsync(Guid userId, string query);
    /// <summary>Throws KeyNotFoundException if the conversation isn't the user's.</summary>
    Task DeleteConversationAsync(Guid userId, Guid conversationId);
}
