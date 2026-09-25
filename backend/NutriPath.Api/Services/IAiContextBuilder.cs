namespace NutriPath.Api.Services;

public interface IAiContextBuilder
{
    Task<string> BuildContextAsync(Guid userId, string userQuestion, ClientClock clock);
}
