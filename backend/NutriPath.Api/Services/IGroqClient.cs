using NutriPath.Api.Models;

namespace NutriPath.Api.Services;

public interface IGroqClient
{
    /// <summary>
    /// Sends one question to the model. <paramref name="history"/> holds earlier turns of the
    /// conversation, oldest first, so follow-up questions make sense.
    /// </summary>
    Task<string> AskAsync(string systemPrompt, string userMessage, IReadOnlyList<GroqMessage>? history = null);
}
