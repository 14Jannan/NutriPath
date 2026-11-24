using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.Models;

namespace NutriPath.Api.Services;

public sealed partial class KnowledgeCli(NutriPathDbContext db, IEmbeddingService embeddings, IConfiguration configuration,
    IServiceScopeFactory scopes, HybridKnowledgeRetrieval retrieval)
{
    private const int MaximumBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RunAsync(string[] args)
    {
        switch (args)
        {
            case ["seed"]:
                var seed = Path.Combine(AppContext.BaseDirectory, "Data", "Seed", "reviewed-knowledge.json");
                var records = JsonSerializer.Deserialize<ImportDocument[]>(await ReadBoundedAsync(seed), JsonOptions)
                    ?? throw new ArgumentException("Invalid reviewed seed.");
                foreach (var record in records)
                {
                    var entry = Validate(record, record.Content);
                    if (await db.KnowledgeDocuments.AnyAsync(d => d.ContentHash == entry.ContentHash)) continue;
                    await using var transaction = await db.Database.BeginTransactionAsync();
                    await SaveAsync(entry, null, true);
                    await transaction.CommitAsync();
                }
                break;
            case ["list"]:
                foreach (var d in await db.KnowledgeDocuments.OrderBy(d => d.Title).ToListAsync())
                    Console.WriteLine(JsonSerializer.Serialize(new { d.Id, d.Title, d.Approved }, JsonOptions));
                break;
            case ["import", var path]:
                await ImportAsync(path, null);
                break;
            case ["update", var id, var path]:
                await ImportAsync(path, Guid.Parse(id));
                break;
            case ["review", var id]:
                var document = await FindAsync(Guid.Parse(id));
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    document.Id, document.Title, document.Content, document.Category, document.Organization,
                    document.SourceUrl, document.PublicationDate, document.ReviewedAtUtc, document.Approved,
                }, JsonOptions));
                break;
            case ["approve", var id]:
                var approved = await FindAsync(Guid.Parse(id));
                await using (var transaction = await db.Database.BeginTransactionAsync())
                {
                    if (embeddings.Enabled) await IndexAsync(approved);
                    approved.Approved = true;
                    approved.ReviewedAtUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                break;
            case ["delete", var id]:
                var deleted = await FindAsync(Guid.Parse(id));
                db.KnowledgeChunks.RemoveRange(deleted.Chunks);
                db.KnowledgeDocuments.Remove(deleted);
                await db.SaveChangesAsync();
                break;
            case ["enable-vector"]:
                await db.Database.ExecuteSqlRawAsync(KnowledgeVectors.SetupSql);
                break;
            case ["coverage"]:
                await ReportCoverageAsync();
                break;
            case ["reindex"]:
                // Idempotent: vectors are upserted per (chunk, model), so reruns only refresh them.
                // Other models' vectors (e.g. MiniLM during an E5 rollout) are left untouched.
                if (!embeddings.Enabled) throw new InvalidOperationException("Configure Knowledge:EmbeddingUrl first.");
                var reindexed = 0;
                var documents = 0;
                foreach (var d in await db.KnowledgeDocuments.Include(d => d.Chunks)
                    .Where(d => d.Approved && d.Language == "en").ToListAsync())
                {
                    await using var transaction = await db.Database.BeginTransactionAsync();
                    await IndexAsync(d);
                    await transaction.CommitAsync();
                    reindexed += d.Chunks.Count;
                    documents++;
                }
                Console.WriteLine(JsonSerializer.Serialize(new { model = embeddings.Model, documents, chunks = reindexed }, JsonOptions));
                await ValidateEmbeddingsAsync();
                break;
            case ["validate-embeddings"]:
                await ValidateEmbeddingsAsync();
                break;
            case ["evaluate-multilingual", var path]:
                await EvaluateMultilingualAsync(path);
                break;
            case ["evaluate-llm", var path, .. var providers]:
                await EvaluateLlmAsync(path, providers);
                break;
            case ["evaluate", var path]:
                var questions = JsonSerializer.Deserialize<EvaluationQuestion[]>(await ReadBoundedAsync(path), JsonOptions)
                    ?? throw new ArgumentException("Invalid evaluation file.");
                var legacyPath = Path.Combine(AppContext.BaseDirectory, "Data", "Seed", "knowledge-base.json");
                var legacyChunks = JsonSerializer.Deserialize<List<KnowledgeChunk>>(await ReadBoundedAsync(legacyPath), JsonOptions) ?? [];
                var baseline = new KnowledgeRetrievalService(scopes);
                baseline.LoadForEvaluation(legacyChunks);
                await retrieval.EnsureIndexBuiltAsync();
                var sriLankanTitles = (await db.KnowledgeDocuments.Where(d =>
                    d.Approved && d.Language == "en" && d.Category == "Sri Lankan nutrition")
                    .Select(d => d.Title).ToListAsync()).ToHashSet();
                var legacyFailures = await EvaluateAsync("legacy-tfidf", questions, sriLankanTitles, q =>
                    Task.FromResult(baseline.Search(q, 5)), legacy: true);
                var keywordFailures = await EvaluateAsync("keyword", questions, sriLankanTitles,
                    q => retrieval.SearchWithModeAsync(q, "keyword", evaluationTopK: 5));
                if (legacyFailures + keywordFailures > 0)
                    throw new InvalidOperationException("Keyword evaluation had retrieval failures.");
                if (embeddings.Enabled)
                {
                    var coverage = await ReportCoverageAsync();
                    EnsureBenchmarkCoverage(coverage.Approved, coverage.Indexed);
                    var semanticFailures = await EvaluateAsync("semantic", questions, sriLankanTitles,
                        q => retrieval.SearchWithModeAsync(q, "semantic", strict: true, evaluationTopK: 5));
                    var hybridFailures = await EvaluateAsync("hybrid", questions, sriLankanTitles,
                        q => retrieval.SearchWithModeAsync(q, "hybrid", strict: true, evaluationTopK: 5));
                    if (semanticFailures + hybridFailures > 0)
                        throw new InvalidOperationException("Semantic or hybrid evaluation had retrieval failures.");
                }
                else Console.WriteLine("Semantic and hybrid benchmarks require Knowledge:EmbeddingUrl and indexed vectors.");
                break;
            default:
                throw new ArgumentException("Commands: seed | list | import FILE | update ID FILE | review ID | approve ID | delete ID | enable-vector | reindex | validate-embeddings | coverage | evaluate FILE | evaluate-multilingual FILE | evaluate-llm FILE [Gemini] [Groq]");
        }
    }

    private async Task<(int Approved, int Indexed)> ReportCoverageAsync()
    {
        var approved = await db.KnowledgeChunks.CountAsync(c =>
            c.Document != null && c.Document.Approved && c.Document.Language == "en");
        var storageReady = await db.Database.SqlQuery<bool>(
            $"""SELECT to_regclass('"KnowledgeEmbeddings"') IS NOT NULL AS "Value" """).SingleAsync();
        var indexed = storageReady ? await KnowledgeVectors.CountIndexedApprovedChunksAsync(db, embeddings.Model) : 0;
        Console.WriteLine(JsonSerializer.Serialize(new { model = embeddings.Model, approved, indexed, storageReady }, JsonOptions));
        return (approved, indexed);
    }

    public static void EnsureBenchmarkCoverage(int approved, int indexed)
    {
        if (approved == 0 || indexed != approved)
            throw new InvalidOperationException("Semantic evaluation requires all approved English chunks to be indexed for the configured model.");
    }

    private static async Task<int> EvaluateAsync(string mode, EvaluationQuestion[] questions, HashSet<string> sriLankanTitles,
        Func<string, Task<List<KnowledgeChunk>>> search, bool legacy = false)
    {
        var evidence = new List<RetrievalJudgment>();
        var noEvidenceReturns = 0;
        var negatives = 0;
        var failures = new Dictionary<string, int>();
        var latency = new List<double>();
        foreach (var q in questions)
        {
            var expected = legacy ? q.LegacyTitles : q.ExpectedTitles;
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            List<string> results;
            try
            {
                results = (await search(q.Question)).Select(c => c.Document?.Title ?? c.Title).Distinct().ToList();
                latency.Add(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or
                InvalidOperationException or Npgsql.NpgsqlException or JsonException or FormatException)
            {
                var type = ex.GetType().Name;
                failures[type] = failures.GetValueOrDefault(type) + 1;
                results = [];
            }
            if (q.ExpectedTitles.Length == 0)
            {
                negatives++;
                if (results.Count > 0) noEvidenceReturns++;
                continue;
            }
            evidence.Add(new(expected, results, q.ExpectedTitles.Any(sriLankanTitles.Contains)));
        }
        if (evidence.Count == 0 || negatives == 0) throw new ArgumentException("Evaluation needs positive and negative cases.");
        latency.Sort();
        double? Percentile(double fraction) =>
            latency.Count == 0 ? null : latency[(int)Math.Ceiling(latency.Count * fraction) - 1];
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            mode, positives = evidence.Count, negatives, failures,
            p50Ms = Percentile(.50), p95Ms = Percentile(.95),
            overall = new[] { Score(evidence, 3), Score(evidence, 5) },
            sriLankan = new[] { Score(evidence.Where(j => j.SriLankan).ToList(), 3),
                Score(evidence.Where(j => j.SriLankan).ToList(), 5) },
            noEvidenceReturnRate = (double)noEvidenceReturns / negatives,
        }, JsonOptions));
        return failures.Values.Sum();
    }

    private static object Score(List<RetrievalJudgment> judgments, int k)
    {
        var recalls = 0d;
        var precisions = 0d;
        var reciprocalRanks = 0d;
        foreach (var judgment in judgments)
        {
            var matches = judgment.Results.Take(k).Select(judgment.Expected.Contains).ToList();
            var hits = matches.Count(match => match);
            recalls += judgment.Expected.Length == 0 ? 0 : (double)hits / judgment.Expected.Length;
            precisions += (double)hits / k;
            var first = matches.IndexOf(true);
            reciprocalRanks += first < 0 ? 0 : 1d / (first + 1);
        }
        var count = judgments.Count;
        return new { k, count, recall = count == 0 ? (double?)null : recalls / count,
            precision = count == 0 ? (double?)null : precisions / count,
            mrr = count == 0 ? (double?)null : reciprocalRanks / count };
    }

    private sealed record RetrievalJudgment(string[] Expected, List<string> Results, bool SriLankan);

    private Task<KnowledgeDocument> FindAsync(Guid id) =>
        db.KnowledgeDocuments.Include(d => d.Chunks).SingleAsync(d => d.Id == id);

    private async Task ImportAsync(string path, Guid? id) =>
        await SaveAsync(await ReadDocumentAsync(path), id, false);

    private async Task SaveAsync(KnowledgeDocument input, Guid? id, bool reviewedSeed)
    {
        if (await db.KnowledgeDocuments.AnyAsync(d => d.ContentHash == input.ContentHash && d.Id != id))
            throw new InvalidOperationException("A document with this content already exists.");

        var document = id is { } existing ? await FindAsync(existing) : new KnowledgeDocument();
        var chunks = Split(input.Content, Math.Clamp(configuration.GetValue("Knowledge:ChunkWords", 120), 32, 180),
            Math.Clamp(configuration.GetValue("Knowledge:OverlapWords", 16), 0, 24));
        db.KnowledgeChunks.RemoveRange(document.Chunks);
        document.Title = input.Title;
        document.Category = input.Category;
        document.Organization = input.Organization;
        document.SourceUrl = input.SourceUrl;
        document.PublicationDate = input.PublicationDate;
        document.ContentHash = input.ContentHash;
        document.Content = input.Content;
        document.Approved = reviewedSeed;
        document.ReviewedAtUtc = reviewedSeed ? DateTime.UtcNow : null;
        document.Chunks = chunks.Select((chunk, i) => new KnowledgeChunk
        {
            Title = chunks.Count == 1 ? input.Title : $"{input.Title} ({i + 1})",
            Content = chunk,
            Category = input.Category,
        }).ToList();
        if (id is null) db.KnowledgeDocuments.Add(document);
        else db.KnowledgeChunks.AddRange(document.Chunks);
        await db.SaveChangesAsync();
        if (reviewedSeed && embeddings.Enabled) await IndexAsync(document);
        Console.WriteLine(document.Id);
    }

    private async Task IndexAsync(KnowledgeDocument document)
    {
        foreach (var batch in document.Chunks.Chunk(16))
        {
            var vectors = await embeddings.EmbedAsync(batch.Select(c => $"{c.Title}\n{c.Content}").ToArray(),
                EmbeddingInputType.Passage);
            for (var i = 0; i < batch.Length; i++)
                await KnowledgeVectors.PutAsync(db, batch[i].Id, embeddings.Model, vectors[i]);
        }
    }

    public static async Task<KnowledgeDocument> ReadDocumentAsync(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".json" or ".md" or ".txt"))
            throw new ArgumentException("Supported document formats: JSON, Markdown and plain text.");
        var metadata = extension == ".json" ? path : path + ".json";
        var input = JsonSerializer.Deserialize<ImportDocument>(await ReadBoundedAsync(metadata), JsonOptions);
        var content = extension == ".json" ? input?.Content : await ReadBoundedAsync(path);
        return Validate(input, content);
    }

    private static KnowledgeDocument Validate(ImportDocument? input, string? content)
    {
        if (input is null || input.Language != "en" ||
            !ValidMetadata(input.Title) || !ValidMetadata(input.Category) || !ValidMetadata(input.Organization) ||
            !IsSourceUrl(input.SourceUrl))
            throw new ArgumentException("Supply English metadata with title, category, organization and an HTTPS source URL.");
        content = Regex.Replace((content ?? "").ReplaceLineEndings("\n"), @"[^\S\n]+", " ").Trim();
        if (content.Length < 20 || content.Any(c => char.IsControl(c) && c != '\n'))
            throw new ArgumentException("Document content is empty, too short or contains control characters.");
        return new KnowledgeDocument
        {
            Title = input.Title!.Trim(), Category = input.Category!.Trim(),
            Organization = input.Organization!.Trim(), SourceUrl = input.SourceUrl!,
            PublicationDate = input.PublicationDate, Content = content,
            ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                Regex.Replace(content, @"\s+", " ")))),
        };
    }

    public static bool IsSourceUrl(string? value) =>
        value is { Length: <= 2048 } && Uri.TryCreate(value, UriKind.Absolute, out var url) &&
        url.Scheme == Uri.UriSchemeHttps && url.UserInfo.Length == 0 && url.HostNameType == UriHostNameType.Dns;

    private static bool ValidMetadata(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && !value.Any(char.IsControl);

    private static async Task<string> ReadBoundedAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var buffer = new byte[MaximumBytes + 1];
        var size = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false);
        if (size > MaximumBytes) throw new ArgumentException("Knowledge file exceeds 256 KiB.");
        return new UTF8Encoding(false, true).GetString(buffer, 0, size).TrimStart('\uFEFF');
    }

    public static List<string> Split(string content, int words, int overlap)
    {
        if (words < 1 || overlap < 0 || overlap >= words) throw new ArgumentOutOfRangeException(nameof(overlap));
        var result = new List<string>();
        var heading = "";
        var pending = new List<string>();
        void Flush()
        {
            if (pending.Count > 0)
                result.Add((heading.Length == 0 ? "" : heading + "\n") + string.Join(" ", pending));
            pending.Clear();
        }
        foreach (var paragraph in Regex.Split(content, @"\n\s*\n|\n(?=\s*#)"))
        {
            var text = paragraph.Trim();
            if (text.StartsWith('#'))
            {
                Flush();
                var lines = text.Split('\n', 2);
                heading = lines[0][..Math.Min(lines[0].Length, 160)];
                text = lines.Length == 2 ? lines[1] : "";
            }
            var terms = Regex.Split(text.Trim(), @"\s+").Where(s => s.Length > 0).ToArray();
            if (pending.Count + terms.Length > words) Flush();
            for (var start = 0; start < terms.Length;)
            {
                var take = Math.Min(words, terms.Length - start);
                pending.AddRange(terms.AsSpan(start, take).ToArray());
                if (start + take == terms.Length) break;
                Flush();
                start += take - overlap;
            }
        }
        Flush();
        return result;
    }

    private sealed record ImportDocument(string? Title, string? Category, string? Organization,
        string? SourceUrl, string? Language, DateOnly? PublicationDate, string? Content);
    private sealed record EvaluationQuestion(string Question, string[] ExpectedTitles, string[] LegacyTitles);
}
