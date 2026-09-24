namespace NutriPath.Api.Services;

public interface IGroqClient
{
    Task<string> AskAsync(string systemPrompt, string userMessage);
}
