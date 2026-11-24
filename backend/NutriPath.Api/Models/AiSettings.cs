namespace NutriPath.Api.Models;

/// <summary>
/// Which LLM provider answers chat questions, and the privacy guard around it.
/// Bound from the "AI" configuration section.
/// </summary>
public sealed class AiSettings
{
    public const string Groq = "Groq";
    public const string Gemini = "Gemini";

    /// <summary>Active provider: "Groq" (default, unchanged behaviour) or "Gemini".</summary>
    public string Provider { get; set; } = Groq;

    /// <summary>
    /// Provider used for users the active provider may not process (see
    /// <see cref="GeminiSettings.RealUserDataApproved"/>). Empty means those users get a
    /// "not available" response instead. It must be set explicitly; nothing is
    /// routed to another provider by default.
    /// </summary>
    public string? UnapprovedUserProvider { get; set; }

    /// <summary>Providers switched off in the admin portal. Requests that would use them are refused.</summary>
    public string[] DisabledProviders { get; set; } = [];

    public MultilingualSettings Multilingual { get; set; } = new();
}

public sealed class MultilingualSettings
{
    /// <summary>When false the assistant answers in English only, as before.</summary>
    public bool Enabled { get; set; } = true;
    public string[] SupportedLanguages { get; set; } = ["en", "ta", "si"];
    public string DefaultLanguage { get; set; } = "en";
}

/// <summary>Google Gemini API settings, bound from the "Gemini" section. The key lives in user secrets or env vars.</summary>
public sealed class GeminiSettings
{
    public string ApiKey { get; set; } = string.Empty;

    // Verified on 2026-11-24 against the project's ListModels response and Google's model,
    // pricing and deprecation pages: free tier available, structured output and thinking
    // supported, no shutdown date announced. gemini-3.1-flash-lite (also free, earliest
    // shutdown 7 May 2027) was the first choice but returned HTTP 503 "high demand" for
    // every request during verification; it can be selected with Gemini:Model.
    public string Model { get; set; } = "gemini-3.5-flash-lite";

    // Trailing slash matters for relative URI resolution.
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";

    public double Temperature { get; set; } = 0.3;
    public int MaxOutputTokens { get; set; } = 1024;

    /// <summary>Gemini 3 thinking level: "minimal", "low", "medium" or "high". Empty sends no thinking config.</summary>
    public string ThinkingLevel { get; set; } = "low";

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Total attempts for retryable failures (429, 500, 502, 503, 504, timeouts).</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Longest single wait between attempts, including a provider Retry-After hint.</summary>
    public int MaxRetryDelaySeconds { get; set; } = 8;

    /// <summary>
    /// The free tier may use prompts and responses to improve Google's products. Until a
    /// privacy assessment approves it, only synthetic or demo accounts may use Gemini.
    /// </summary>
    public bool FreeTier { get; set; } = true;
    public bool RealUserDataApproved { get; set; }

    /// <summary>Email domains of synthetic demo accounts (e.g. "nutripath.test") that may use the free tier.</summary>
    public string[] SyntheticUserEmailDomains { get; set; } = ["nutripath.test"];

    /// <summary>Individually approved test accounts (lower-case email addresses).</summary>
    public string[] ApprovedUserEmails { get; set; } = [];
}
