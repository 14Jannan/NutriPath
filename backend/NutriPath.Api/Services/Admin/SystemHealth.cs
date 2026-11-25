using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NutriPath.Api.Data;

namespace NutriPath.Api.Services.Admin;

public sealed record ComponentHealth(string Name, string Status, int? LatencyMs, string? Detail, DateTime CheckedAtUtc);

/// <summary>
/// Cached (60 s) health of local dependencies. External AI and USDA APIs are not called here,
/// so refreshing the dashboard never spends provider quota; their status comes from the most
/// recent explicit connection test and from real request metrics.
/// Statuses: Healthy, Degraded, Unavailable, RateLimited, Unknown. Details never include
/// connection strings, stack traces or secrets.
/// </summary>
public sealed class SystemHealth(IServiceScopeFactory scopes, IMemoryCache cache, IHttpClientFactory http,
    IConfiguration configuration, IProviderCredentialStore credentials)
{
    public Task<IReadOnlyList<ComponentHealth>> SummaryAsync(CancellationToken ct, bool refresh = false)
    {
        if (refresh) cache.Remove("system-health");
        return cache.GetOrCreateAsync<IReadOnlyList<ComponentHealth>>("system-health", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            return await CheckAsync(ct);
        })!;
    }

    private async Task<IReadOnlyList<ComponentHealth>> CheckAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var results = new List<ComponentHealth> { new("API", "Healthy", 0, null, now) };
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();

        var sw = Stopwatch.StartNew();
        try
        {
            var reachable = await db.Database.CanConnectAsync(ct);
            results.Add(new("PostgreSQL", reachable ? "Healthy" : "Unavailable", (int)sw.ElapsedMilliseconds, null, now));
            if (reachable && db.Database.IsNpgsql())
            {
                var vector = await db.Database.SqlQuery<bool>($"""SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'vector') AS "Value" """).SingleAsync(ct);
                results.Add(new("pgvector", vector ? "Healthy" : "Degraded", null, vector ? null : "Extension not installed; keyword retrieval only.", now));
            }
        }
        catch (Exception ex) when (ex is Npgsql.NpgsqlException or InvalidOperationException or TimeoutException)
        {
            results.Add(new("PostgreSQL", "Unavailable", (int)sw.ElapsedMilliseconds, "Connection failed.", now));
        }

        var embedUrl = configuration["Knowledge:EmbeddingUrl"];
        if (string.IsNullOrWhiteSpace(embedUrl)) results.Add(new("Embedding worker", "Unknown", null, "Not configured (Knowledge:EmbeddingUrl).", now));
        else
        {
            sw.Restart();
            try
            {
                var ready = new Uri(new Uri(embedUrl), "/ready");
                var client = http.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                using var response = await client.GetAsync(ready, ct);
                results.Add(new("Embedding worker", response.IsSuccessStatusCode ? "Healthy" : "Degraded", (int)sw.ElapsedMilliseconds,
                    response.IsSuccessStatusCode ? null : "Model not ready.", now));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
            {
                results.Add(new("Embedding worker", "Unavailable", (int)sw.ElapsedMilliseconds, "Not reachable.", now));
            }
        }

        var since = now.AddHours(-1);
        foreach (var provider in new[] { ExternalProviders.Gemini, ExternalProviders.Groq, ExternalProviders.Usda })
        {
            var key = await credentials.GetActiveCredentialAsync(provider, ct);
            if (key is null)
            {
                results.Add(new(provider, "Unavailable", null, "No API key configured.", now));
                continue;
            }
            var lastTest = await db.AdminAuditLogs.AsNoTracking().Where(a => a.Action == "credential.test" && a.ResourceId == provider)
                .OrderByDescending(a => a.AtUtc).Select(a => new { a.Outcome, a.Summary, a.AtUtc }).FirstOrDefaultAsync(ct);
            var recent = provider == ExternalProviders.Usda ? [] : await db.AiRequestMetrics.AsNoTracking()
                .Where(m => m.Provider == provider && m.AtUtc > since).Select(m => m.Outcome).ToListAsync(ct);
            var status = recent.Count > 0
                ? recent.Count(o => o == "RateLimited") * 2 > recent.Count ? "RateLimited"
                : recent.Count(o => o != "Success") * 2 > recent.Count ? "Degraded" : "Healthy"
                : lastTest is null ? "Unknown" : lastTest.Outcome == "Success" ? "Healthy" : lastTest.Summary == "RateLimited" ? "RateLimited" : "Degraded";
            var detail = recent.Count > 0
                ? $"{recent.Count(o => o != "Success")} of {recent.Count} requests failed in the last hour."
                : lastTest is null ? "No traffic or connection test yet." : $"Last connection test: {lastTest.Summary} at {lastTest.AtUtc:u}.";
            results.Add(new(provider, status, null, detail, now));
        }

        var stuck = await db.BackgroundJobs.CountAsync(j => j.Status == "Running" && j.StartedAtUtc < now.AddMinutes(-30), ct);
        var failed = await db.BackgroundJobs.CountAsync(j => j.Status == "Failed" && j.FinishedAtUtc > now.AddHours(-24), ct);
        results.Add(new("Background jobs", stuck > 0 ? "Degraded" : "Healthy", null,
            stuck > 0 ? $"{stuck} job(s) running for over 30 minutes." : failed > 0 ? $"{failed} failed in the last 24 hours." : null, now));
        return results;
    }
}
