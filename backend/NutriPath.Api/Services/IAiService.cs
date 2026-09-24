namespace NutriPath.Api.Services;

public interface IAiService
{
    Task<string> ChatAsync(Guid userId, string userMessage);
}
