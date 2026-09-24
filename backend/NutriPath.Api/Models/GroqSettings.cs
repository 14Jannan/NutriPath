namespace NutriPath.Api.Models;

public class GroqSettings
{
    public string ApiKey { get; set; } = string.Empty;
    // Overridable via the "Groq:Model" config key if Groq's catalog changes.
    public string Model { get; set; } = "openai/gpt-oss-120b";

    // Trailing slash matters: HttpClient resolves relative paths against
    // BaseAddress like a browser does, so without it "/openai/v1" would
    // be dropped when combined with "chat/completions".
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1/";
}
