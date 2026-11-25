using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.Models.Admin;

namespace NutriPath.Api.Services.Admin;

/// <summary>
/// Retrieval benchmark runs over the repository's question sets (docs/evaluation), the same data
/// as `knowledge evaluate`. Only retrieval is measured: no model is called, so runs are free and
/// use no user data. Scores: recall, precision and MRR at k, plus how often questions that need no
/// knowledge (personal, medical, prompt injection) still returned some.
/// </summary>
public sealed class EvaluationService(NutriPathDbContext db, HybridKnowledgeRetrieval retrieval, IEmbeddingService embeddings,
    BackgroundJobs jobs, AdminAudit audit)
{
    public static readonly IReadOnlyList<string> Datasets = ["nutrition", "multilingual"];
    public static readonly IReadOnlyList<string> Modes = ["keyword", "semantic", "hybrid"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record Question(string Id, string Text, string[] ExpectedTitles, string Language, string Category);

    public static string DatasetPath(string dataset) =>
        Path.Combine(AppContext.BaseDirectory, "Data", "Evaluation", dataset == "multilingual" ? "multilingual-questions.json" : "nutrition-questions.json");

    public static async Task<List<Question>> LoadAsync(string dataset, CancellationToken ct)
    {
        var path = DatasetPath(dataset);
        if (!File.Exists(path)) throw new AdminRuleException("The evaluation question set isn't deployed with this build.");
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
        var root = json.RootElement;
        if (dataset == "multilingual")
            return root.GetProperty("questions").EnumerateArray().Select(q => new Question(
                q.GetProperty("id").GetString()!, q.GetProperty("question").GetString()!,
                q.GetProperty("expectedTitles").EnumerateArray().Select(t => t.GetString()!).ToArray(),
                q.GetProperty("language").GetString()!, q.GetProperty("category").GetString()!)).ToList();
        return root.EnumerateArray().Select((q, i) => new Question(
            $"n{i + 1:000}", q.GetProperty("question").GetString()!,
            q.GetProperty("expectedTitles").EnumerateArray().Select(t => t.GetString()!).ToArray(), "en", "nutrition")).ToList();
    }

    public async Task<EvaluationRun> StartAsync(string dataset, string mode, int topK, Guid actor, CancellationToken ct)
    {
        if (!Datasets.Contains(dataset) || !Modes.Contains(mode)) throw new AdminRuleException("Choose a valid dataset and mode.", StatusCodes.Status400BadRequest);
        if (mode != "keyword" && (!embeddings.Enabled || !db.Database.IsNpgsql()))
            throw new AdminRuleException("Semantic and hybrid runs need the embedding worker and PostgreSQL with pgvector.");
        var run = new EvaluationRun { Dataset = dataset, Mode = mode, TopK = Math.Clamp(topK, 1, 10), EmbeddingModel = mode == "keyword" ? null : embeddings.Model, CreatedByUserId = actor };
        db.EvaluationRuns.Add(run);
        await db.SaveChangesAsync(ct);
        run.JobId = await jobs.EnqueueAsync(BackgroundJobs.EvaluationRetrieval, new { runId = run.Id }, actor, ct);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("evaluation.start", resourceType: "EvaluationRun", resourceId: run.Id.ToString(), summary: $"{dataset}, {mode}, top {run.TopK}", cancellationToken: ct);
        return run;
    }

    /// <summary>Runs inside the background job.</summary>
    public async Task<object> ExecuteAsync(Guid runId, Func<int, Task> progress, CancellationToken ct)
    {
        var run = await db.EvaluationRuns.SingleAsync(r => r.Id == runId, ct);
        run.Status = "Running";
        await db.SaveChangesAsync(ct);
        try
        {
            var questions = await LoadAsync(run.Dataset, ct);
            await retrieval.EnsureIndexBuiltAsync();
            var results = new List<object>();
            var positives = new List<(string[] Expected, List<string> Found, string Language)>();
            int negatives = 0, negativeHits = 0, errors = 0;
            var latency = new List<double>();
            for (var i = 0; i < questions.Count; i++)
            {
                var q = questions[i];
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                List<string> found;
                try
                {
                    found = (await retrieval.SearchWithModeAsync(q.Text, run.Mode, strict: run.Mode != "keyword", evaluationTopK: run.TopK))
                        .Select(c => c.Document?.Title ?? c.Title).Distinct().ToList();
                    latency.Add(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or Npgsql.NpgsqlException or TaskCanceledException)
                {
                    errors++;
                    found = [];
                }
                var hit = q.ExpectedTitles.Length == 0 ? found.Count == 0 : found.Take(run.TopK).Any(q.ExpectedTitles.Contains);
                if (q.ExpectedTitles.Length == 0) { negatives++; if (found.Count > 0) negativeHits++; }
                else positives.Add((q.ExpectedTitles, found, q.Language));
                results.Add(new { q.Id, question = q.Text, q.Language, q.Category, expected = q.ExpectedTitles, found = found.Take(run.TopK), pass = hit });
                if (i % 10 == 0) await progress((int)((i + 1) * 100.0 / questions.Count));
            }
            latency.Sort();
            double? P(double f) => latency.Count == 0 ? null : Math.Round(latency[(int)Math.Ceiling(latency.Count * f) - 1], 1);
            var metrics = new
            {
                questions = questions.Count, positives = positives.Count, negatives, errors,
                overall = Score(positives, run.TopK),
                byLanguage = positives.GroupBy(p => p.Language).ToDictionary(g => g.Key, g => Score(g.ToList(), run.TopK)),
                noEvidenceReturnRate = negatives == 0 ? (double?)null : Math.Round((double)negativeHits / negatives, 3),
                p50Ms = P(.5), p95Ms = P(.95),
            };
            run.MetricsJson = JsonSerializer.Serialize(metrics, Json);
            run.ResultsJson = JsonSerializer.Serialize(results, Json);
            run.Status = "Succeeded";
            run.FinishedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return new { runId, metrics.overall };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Status = "Failed";
            run.Error = ex is AdminRuleException ? ex.Message : ex.GetType().Name;
            run.FinishedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private static object Score(List<(string[] Expected, List<string> Found, string Language)> judgments, int k)
    {
        double recall = 0, precision = 0, mrr = 0;
        foreach (var (expected, found, _) in judgments)
        {
            var matches = found.Take(k).Select(expected.Contains).ToList();
            var hits = matches.Count(m => m);
            recall += (double)hits / expected.Length;
            precision += (double)hits / k;
            var first = matches.IndexOf(true);
            mrr += first < 0 ? 0 : 1d / (first + 1);
        }
        var n = judgments.Count;
        return new
        {
            k, count = n,
            recall = n == 0 ? (double?)null : Math.Round(recall / n, 3),
            precision = n == 0 ? (double?)null : Math.Round(precision / n, 3),
            mrr = n == 0 ? (double?)null : Math.Round(mrr / n, 3),
        };
    }
}
