using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Models.Admin;

namespace NutriPath.Api.Services.Admin;

/// <summary>
/// Durable, database-backed jobs (no extra infrastructure). A job is claimed atomically (Queued →
/// Running with a conditional update), so several API instances can run the worker safely.
/// Failed jobs retry up to 3 times; Cancel is honoured between batches. Errors are stored as a
/// short category, never a stack trace.
/// </summary>
public sealed class BackgroundJobs(NutriPathDbContext db, IServiceProvider services, ILogger<BackgroundJobs> logger)
{
    public const string KnowledgeIndex = "knowledge.index";
    public const string KnowledgeReindexAll = "knowledge.reindex-all";
    public const string UsdaSync = "usda.sync";
    public const int MaxAttempts = 3;
    public static readonly IReadOnlyList<string> Types = [KnowledgeIndex, KnowledgeReindexAll, UsdaSync];

    public async Task<Guid> EnqueueAsync(string type, object payload, Guid? actor, CancellationToken ct)
    {
        var job = new BackgroundJob { Type = type, PayloadJson = JsonSerializer.Serialize(payload), CreatedByUserId = actor };
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync(ct);
        return job.Id;
    }

    /// <summary>Claims and runs the oldest queued job. Returns false when there is nothing to do.</summary>
    public async Task<bool> RunNextAsync(CancellationToken ct)
    {
        var candidate = await db.BackgroundJobs.Where(j => j.Status == "Queued").OrderBy(j => j.CreatedAtUtc)
            .Select(j => j.Id).FirstOrDefaultAsync(ct);
        if (candidate == Guid.Empty) return false;

        var now = DateTime.UtcNow;
        if (db.Database.IsRelational())
        {
            var claimed = await db.BackgroundJobs.Where(j => j.Id == candidate && j.Status == "Queued")
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, "Running").SetProperty(j => j.StartedAtUtc, now)
                    .SetProperty(j => j.Attempts, j => j.Attempts + 1), ct);
            if (claimed == 0) return true; // another worker took it
        }
        var job = await db.BackgroundJobs.SingleAsync(j => j.Id == candidate, ct);
        await db.Entry(job).ReloadAsync(ct);
        if (!db.Database.IsRelational())
        {
            if (job.Status != "Queued") return true;
            job.Status = "Running";
            job.StartedAtUtc = now;
            job.Attempts++;
            await db.SaveChangesAsync(ct);
        }

        try
        {
            var result = await ExecuteAsync(job, ct);
            job.Status = job.CancelRequested ? "Cancelled" : "Succeeded";
            job.Progress = 100;
            job.ResultJson = result is null ? null : Truncate(JsonSerializer.Serialize(result), 1000);
            job.Error = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Background job {JobId} ({Type}) failed: {ErrorType}.", job.Id, job.Type, ex.GetType().Name);
            job.Error = ex switch
            {
                AdminRuleException rule => rule.Message,
                HttpRequestException => "External service unavailable.",
                Npgsql.NpgsqlException => "Database error.",
                InvalidOperationException e when e.Message.Length < 200 => e.Message,
                _ => ex.GetType().Name,
            };
            job.Status = job.Attempts < MaxAttempts && !job.CancelRequested ? "Queued" : "Failed";
        }
        job.FinishedAtUtc = job.Status is "Queued" ? null : DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        return true;
    }

    private async Task<object?> ExecuteAsync(BackgroundJob job, CancellationToken ct)
    {
        using var payload = JsonDocument.Parse(job.PayloadJson);
        switch (job.Type)
        {
            case KnowledgeIndex:
            {
                var id = payload.RootElement.GetProperty("documentId").GetGuid();
                var document = await db.KnowledgeDocuments.Include(d => d.Chunks).SingleOrDefaultAsync(d => d.Id == id, ct)
                    ?? throw new AdminRuleException("The document no longer exists.");
                if (!document.Approved) throw new AdminRuleException("Only approved documents are indexed.");
                await IndexAsync([document], job, ct);
                return new { documents = 1, chunks = document.Chunks.Count };
            }
            case KnowledgeReindexAll:
            {
                var documents = await db.KnowledgeDocuments.Include(d => d.Chunks).Where(d => d.Approved && d.Language == "en").ToListAsync(ct);
                await IndexAsync(documents, job, ct);
                return new { documents = documents.Count, chunks = documents.Sum(d => d.Chunks.Count) };
            }
            case UsdaSync:
            {
                var query = payload.RootElement.GetProperty("query").GetString() ?? "";
                var pageSize = payload.RootElement.TryGetProperty("pageSize", out var p) ? p.GetInt32() : 10;
                var sync = services.GetRequiredService<IUsdaFoodSyncService>();
                var result = await sync.SyncAsync(query, Math.Clamp(pageSize, 1, 50));
                if (result.Status == SyncJobStatus.Failed) throw new AdminRuleException("USDA sync failed. See the sync history for details.");
                return new { syncJobId = result.Id, result.RecordsFetched, result.RecordsInserted, result.RecordsUpdated, result.RecordsFailed };
            }
            default:
                throw new AdminRuleException($"Unknown job type {job.Type}.");
        }
    }

    private async Task IndexAsync(List<KnowledgeDocument> documents, BackgroundJob job, CancellationToken ct)
    {
        var embeddings = services.GetRequiredService<IEmbeddingService>();
        if (!embeddings.Enabled) throw new AdminRuleException("The embedding worker is not configured (Knowledge:EmbeddingUrl).");
        if (!db.Database.IsNpgsql()) throw new AdminRuleException("Vector indexing needs PostgreSQL with pgvector.");
        var cli = services.GetRequiredService<KnowledgeCli>();
        for (var i = 0; i < documents.Count; i++)
        {
            await db.Entry(job).ReloadAsync(ct);
            if (job.CancelRequested) return;
            await cli.IndexAsync(documents[i]); // same code as `knowledge reindex`: passage prefixes, upsert per model
            job.Progress = (int)((i + 1) * 100.0 / Math.Max(1, documents.Count));
            await db.SaveChangesAsync(ct);
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>Polls for queued jobs every few seconds. Disabled in the Testing environment (tests run jobs directly).</summary>
public sealed class BackgroundJobWorker(IServiceScopeFactory scopes, IHostEnvironment environment, ILogger<BackgroundJobWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (environment.IsEnvironment("Testing")) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var jobs = scope.ServiceProvider.GetRequiredService<BackgroundJobs>();
                if (await jobs.RunNextAsync(stoppingToken)) continue;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Background job polling failed ({ErrorType}).", ex.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
