using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NutriPath.Api.Data;
using NutriPath.Api.Models.Admin;

namespace NutriPath.Api.Services.Admin;

/// <summary>External services whose API keys can be managed from the admin portal.</summary>
public static class ExternalProviders
{
    public const string Gemini = "Gemini";
    public const string Groq = "Groq";
    public const string Usda = "Usda";
    public static readonly IReadOnlyList<string> All = [Gemini, Groq, Usda];

    public static string? Normalize(string? provider) =>
        All.FirstOrDefault(p => string.Equals(p, provider, StringComparison.OrdinalIgnoreCase));

    /// <summary>The environment/user-secrets key used before any key is managed at runtime.</summary>
    public static string ConfigKey(string provider) => $"{provider}:ApiKey";
}

/// <summary>Resolves the API key a provider client should use right now.</summary>
public interface IProviderCredentialStore
{
    Task<string?> GetActiveCredentialAsync(string provider, CancellationToken cancellationToken);
    void Invalidate(string provider);
}

/// <summary>
/// Runtime-managed, encrypted credentials take precedence. Once a provider has any stored
/// credential (active, inactive or revoked), the environment key is no longer used for it, so a
/// key that was replaced or revoked in the portal is never silently brought back.
/// Cached for 30 seconds; this instance invalidates immediately on change, others within 30 s.
/// </summary>
public sealed class ProviderCredentialStore(IServiceScopeFactory scopes, ISecretProtector protector,
    IConfiguration configuration, IMemoryCache cache) : IProviderCredentialStore
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    public async Task<string?> GetActiveCredentialAsync(string provider, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(("credential", provider), out string? cached)) return cached;
        string? value;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();
            var managed = await db.ProviderCredentials.AsNoTracking().AnyAsync(c => c.Provider == provider, cancellationToken);
            if (!managed) value = configuration[ExternalProviders.ConfigKey(provider)];
            else
            {
                var active = await db.ProviderCredentials.AsNoTracking()
                    .Where(c => c.Provider == provider && c.Status == CredentialStatus.Active)
                    .OrderByDescending(c => c.Version).FirstOrDefaultAsync(cancellationToken);
                value = active is null ? null : protector.Unprotect(
                    new ProtectedSecret(active.Ciphertext, active.Nonce, active.Tag, active.KeyVersion), Aad(active.Provider, active.Version));
            }
        }
        value = string.IsNullOrWhiteSpace(value) ? null : value;
        cache.Set(("credential", provider), value, CacheFor);
        return value;
    }

    public void Invalidate(string provider) => cache.Remove(("credential", provider));

    public static string Aad(string provider, int version) => $"credential:{provider}:{version}";
}

public sealed record CredentialTestResult(bool Ok, string Category, int? HttpStatus = null);

/// <summary>Checks a key with a minimal request that costs no generation tokens.</summary>
public interface IProviderConnectionTester
{
    Task<CredentialTestResult> TestAsync(string provider, string apiKey, CancellationToken cancellationToken);
}

/// <summary>
/// Fixed, allow-listed endpoints only (no caller-supplied URLs): Gemini and Groq model lists and
/// a one-result USDA search. Results are reduced to a category; provider error bodies are dropped.
/// </summary>
public sealed class ProviderConnectionTester(HttpClient http) : IProviderConnectionTester
{
    public async Task<CredentialTestResult> TestAsync(string provider, string apiKey, CancellationToken cancellationToken)
    {
        using var request = provider switch
        {
            ExternalProviders.Gemini => Get("https://generativelanguage.googleapis.com/v1beta/models?pageSize=1", r => r.Headers.Add("x-goog-api-key", apiKey)),
            ExternalProviders.Groq => Get("https://api.groq.com/openai/v1/models", r => r.Headers.Authorization = new("Bearer", apiKey)),
            ExternalProviders.Usda => Get($"https://api.nal.usda.gov/fdc/v1/foods/search?query=apple&pageSize=1&api_key={Uri.EscapeDataString(apiKey)}", _ => { }),
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await http.SendAsync(request, timeout.Token);
            var status = (int)response.StatusCode;
            return response.StatusCode switch
            {
                _ when response.IsSuccessStatusCode => new(true, "Healthy", status),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new(false, "InvalidCredential", status),
                HttpStatusCode.BadRequest when provider == ExternalProviders.Gemini => new(false, "InvalidCredential", status),
                HttpStatusCode.TooManyRequests => new(false, "RateLimited", status),
                _ => new(false, "Unavailable", status),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new(false, "Unreachable");
        }

        static HttpRequestMessage Get(string url, Action<HttpRequestMessage> configure)
        {
            var message = new HttpRequestMessage(HttpMethod.Get, url);
            configure(message);
            return message;
        }
    }
}

/// <summary>Replace, test, roll back and revoke provider keys. The plaintext key never leaves this class.</summary>
public sealed class ProviderCredentialService(NutriPathDbContext db, ISecretProtector protector,
    IProviderCredentialStore store, IProviderConnectionTester tester, AdminAudit audit, IConfiguration configuration)
{
    public async Task<object> StatusAsync(string provider, CancellationToken ct)
    {
        var rows = await db.ProviderCredentials.AsNoTracking().Where(c => c.Provider == provider)
            .OrderByDescending(c => c.Version).ToListAsync(ct);
        var active = rows.FirstOrDefault(r => r.Status == CredentialStatus.Active);
        var environment = !string.IsNullOrWhiteSpace(configuration[ExternalProviders.ConfigKey(provider)]);
        var actors = await ActorEmailsAsync(rows.Select(r => r.CreatedByUserId), ct);
        return new
        {
            provider,
            source = active is not null ? "Runtime" : rows.Count > 0 ? "Disabled" : environment ? "Environment" : "Missing",
            configured = active is not null || (rows.Count == 0 && environment),
            hint = active is null ? null : "••••" + active.Hint,
            activeVersion = active?.Version,
            activatedAtUtc = active?.ActivatedAtUtc,
            lastValidatedAtUtc = active?.LastValidatedAtUtc,
            updatedBy = active?.CreatedByUserId is { } by ? actors.GetValueOrDefault(by) : null,
            encryptionAvailable = protector.Configured,
            history = rows.Select(r => new
            {
                r.Id, r.Version, hint = "••••" + r.Hint, r.Status, r.CreatedAtUtc, r.ActivatedAtUtc, r.LastValidatedAtUtc,
                createdBy = r.CreatedByUserId is { } c ? actors.GetValueOrDefault(c) : null,
            }),
        };
    }

    /// <summary>Tests the candidate first; only a working key is encrypted and activated (atomically).</summary>
    public async Task<CredentialTestResult> ReplaceAsync(string provider, string candidate, Guid actor, CancellationToken ct)
    {
        candidate = candidate?.Trim() ?? "";
        if (candidate.Length is < 10 or > 500 || candidate.Any(char.IsWhiteSpace) || candidate.Any(char.IsControl))
            throw new AdminRuleException("That doesn't look like an API key.", StatusCodes.Status400BadRequest);
        if (!protector.Configured)
            throw new AdminRuleException("Credential encryption is not configured on the server.", StatusCodes.Status503ServiceUnavailable);

        var test = await tester.TestAsync(provider, candidate, ct);
        if (!test.Ok)
        {
            await audit.WriteAsync("credential.replace", "Failure", "ProviderCredential", provider,
                $"Candidate rejected ({test.Category}); existing credential kept", cancellationToken: ct);
            return test;
        }

        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        var version = (await db.ProviderCredentials.Where(c => c.Provider == provider).MaxAsync(c => (int?)c.Version, ct) ?? 0) + 1;
        var secret = protector.Protect(candidate, ProviderCredentialStore.Aad(provider, version));
        var now = DateTime.UtcNow;
        foreach (var old in await db.ProviderCredentials.Where(c => c.Provider == provider && c.Status == CredentialStatus.Active).ToListAsync(ct))
            old.Status = CredentialStatus.Inactive;
        db.ProviderCredentials.Add(new ProviderCredential
        {
            Provider = provider, Version = version, Ciphertext = secret.Ciphertext, Nonce = secret.Nonce, Tag = secret.Tag,
            KeyVersion = secret.KeyVersion, Hint = candidate[^4..], Status = CredentialStatus.Active,
            CreatedAtUtc = now, ActivatedAtUtc = now, LastValidatedAtUtc = now, CreatedByUserId = actor,
        });
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        store.Invalidate(provider);
        await audit.WriteAsync("credential.replace", resourceType: "ProviderCredential", resourceId: provider,
            summary: $"Version {version} (••••{candidate[^4..]}) tested and activated", cancellationToken: ct);
        return test;
    }

    /// <summary>Reactivates an earlier, not-revoked version after testing it again.</summary>
    public async Task<CredentialTestResult> RollbackAsync(string provider, Guid credentialId, CancellationToken ct)
    {
        var target = await db.ProviderCredentials.SingleOrDefaultAsync(c => c.Id == credentialId && c.Provider == provider, ct)
            ?? throw new KeyNotFoundException();
        if (target.Status == CredentialStatus.Revoked)
            throw new AdminRuleException("Revoked keys can't be reactivated. Add a new key instead.");
        var plaintext = protector.Unprotect(new ProtectedSecret(target.Ciphertext, target.Nonce, target.Tag, target.KeyVersion),
            ProviderCredentialStore.Aad(provider, target.Version));
        var test = await tester.TestAsync(provider, plaintext, ct);
        if (!test.Ok)
        {
            await audit.WriteAsync("credential.rollback", "Failure", "ProviderCredential", provider, $"Version {target.Version} failed ({test.Category})", cancellationToken: ct);
            return test;
        }
        foreach (var old in await db.ProviderCredentials.Where(c => c.Provider == provider && c.Status == CredentialStatus.Active).ToListAsync(ct))
            old.Status = CredentialStatus.Inactive;
        target.Status = CredentialStatus.Active;
        target.ActivatedAtUtc = target.LastValidatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        store.Invalidate(provider);
        await audit.WriteAsync("credential.rollback", resourceType: "ProviderCredential", resourceId: provider, summary: $"Version {target.Version} reactivated", cancellationToken: ct);
        return test;
    }

    /// <summary>Marks a version as compromised in NutriPath. It must also be revoked at the provider.</summary>
    public async Task RevokeAsync(string provider, Guid credentialId, string reason, CancellationToken ct)
    {
        var target = await db.ProviderCredentials.SingleOrDefaultAsync(c => c.Id == credentialId && c.Provider == provider, ct)
            ?? throw new KeyNotFoundException();
        target.Status = CredentialStatus.Revoked;
        await db.SaveChangesAsync(ct);
        store.Invalidate(provider);
        await audit.WriteAsync("credential.revoke", resourceType: "ProviderCredential", resourceId: provider,
            summary: $"Version {target.Version} revoked in NutriPath (revoke it at the provider too)", reason: reason, cancellationToken: ct);
    }

    /// <summary>Tests the key currently in use (runtime or environment).</summary>
    public async Task<CredentialTestResult> TestActiveAsync(string provider, CancellationToken ct)
    {
        var key = await store.GetActiveCredentialAsync(provider, ct);
        if (key is null) return new CredentialTestResult(false, "Missing");
        var result = await tester.TestAsync(provider, key, ct);
        if (result.Ok)
        {
            var active = await db.ProviderCredentials.FirstOrDefaultAsync(c => c.Provider == provider && c.Status == CredentialStatus.Active, ct);
            if (active is not null)
            {
                active.LastValidatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }
        await audit.WriteAsync("credential.test", result.Ok ? "Success" : "Failure", "ProviderCredential", provider, result.Category, cancellationToken: ct);
        return result;
    }

    private async Task<Dictionary<Guid, string>> ActorEmailsAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.OfType<Guid>().Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
    }
}
