using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NutriPath.Api.Data;
using NutriPath.Api.Models;

namespace NutriPath.Api.Services.Llm;

public interface ILlmProviderSelector
{
    /// <summary>
    /// The provider allowed to process this user's data. Throws
    /// <see cref="LlmProviderNotPermittedException"/> when none is.
    /// </summary>
    Task<ILlmClient> SelectAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Always the same provider. Used by tests and single-provider setups.</summary>
public sealed class FixedLlmProviderSelector(ILlmClient client) : ILlmProviderSelector
{
    public Task<ILlmClient> SelectAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(client);
}

/// <summary>
/// Picks the configured provider and enforces the free-tier privacy guard: Gemini's free
/// tier may use submitted data to improve Google's products, so until
/// <see cref="GeminiSettings.RealUserDataApproved"/> is set only synthetic demo accounts
/// reach it. Other users go to <see cref="AiSettings.UnapprovedUserProvider"/> only when
/// that is configured explicitly; otherwise they are refused.
/// </summary>
public sealed class LlmProviderSelector(
    IOptions<AiSettings> ai,
    IOptions<GeminiSettings> gemini,
    NutriPathDbContext db,
    IServiceProvider services) : ILlmProviderSelector
{
    public async Task<ILlmClient> SelectAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var provider = Normalize(ai.Value.Provider)
            ?? throw new LlmProviderNotPermittedException($"Unknown AI provider '{ai.Value.Provider}'.");
        if (provider == AiSettings.Groq) return Resolve(AiSettings.Groq);

        if (await GeminiMayProcessAsync(userId, cancellationToken)) return Resolve(AiSettings.Gemini);

        return Normalize(ai.Value.UnapprovedUserProvider) switch
        {
            AiSettings.Groq => Resolve(AiSettings.Groq),
            _ => throw new LlmProviderNotPermittedException(
                "Gemini free tier is limited to synthetic accounts and no alternative provider is enabled."),
        };
    }

    public async Task<bool> GeminiMayProcessAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var settings = gemini.Value;
        if (!settings.FreeTier || settings.RealUserDataApproved) return true;
        var email = await db.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleOrDefaultAsync(cancellationToken);
        return IsSyntheticOrApproved(email, settings);
    }

    public static bool IsSyntheticOrApproved(string? email, GeminiSettings settings)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        email = email.Trim().ToLowerInvariant();
        if (settings.ApprovedUserEmails.Any(a => string.Equals(a.Trim(), email, StringComparison.OrdinalIgnoreCase)))
            return true;
        var at = email.LastIndexOf('@');
        if (at < 0) return false;
        var domain = email[(at + 1)..];
        return settings.SyntheticUserEmailDomains.Any(d =>
            string.Equals(d.Trim().TrimStart('@'), domain, StringComparison.OrdinalIgnoreCase));
    }

    private ILlmClient Resolve(string provider) => provider == AiSettings.Gemini
        ? services.GetRequiredService<GeminiClient>()
        : services.GetRequiredService<GroqLlmClient>();

    private static string? Normalize(string? provider) => provider?.Trim().ToLowerInvariant() switch
    {
        "groq" => AiSettings.Groq,
        "gemini" => AiSettings.Gemini,
        _ => null,
    };
}
