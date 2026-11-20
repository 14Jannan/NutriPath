using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using Npgsql;

namespace NutriPath.Api.Services;

public sealed class HybridKnowledgeRetrieval(
    KnowledgeRetrievalService keyword,
    NutriPathDbContext db,
    IEmbeddingService embeddings,
    IConfiguration configuration,
    ILogger<HybridKnowledgeRetrieval> logger) : IKnowledgeRetrievalService
{
    public Task EnsureIndexBuiltAsync() => keyword.EnsureIndexBuiltAsync();
    public List<KnowledgeChunk> Search(string query, int topK) => keyword.Search(query, topK);

    public Task<List<KnowledgeChunk>> SearchAsync(string query) =>
        SearchWithModeAsync(query, configuration["Knowledge:RetrievalMode"] ?? "hybrid");

    public async Task<List<KnowledgeChunk>> SearchWithModeAsync(string query, string mode, bool strict = false)
    {
        var topK = Math.Clamp(configuration.GetValue("Knowledge:TopK", 3), 1, 12);
        var count = Math.Clamp(configuration.GetValue("Knowledge:CandidateCount", 12), topK, 50);
        var minimumKeyword = Math.Clamp(configuration.GetValue("Knowledge:MinimumKeywordSimilarity", 0d), 0, 1);
        var keywords = keyword.Search(query, count, minimumKeyword).Select(c => c.Id).ToList();
        List<Guid> semantic = [];
        if (mode != "keyword" && embeddings.Enabled && db.Database.IsNpgsql())
        {
            try
            {
                var vectors = await embeddings.EmbedAsync([query]);
                semantic = await KnowledgeVectors.SearchAsync(db, vectors[0], embeddings.Model,
                    Math.Clamp(configuration.GetValue("Knowledge:MaxVectorDistance", 0.65), 0, 2), count);
            }
            catch (Exception ex) when (!strict && ex is (HttpRequestException or OperationCanceledException or
                InvalidOperationException or NpgsqlException or System.Text.Json.JsonException or FormatException))
            {
                logger.LogWarning("Semantic nutrition retrieval unavailable ({ErrorType}); using keyword search.", ex.GetType().Name);
            }
        }

        var ranked = mode == "semantic" && (semantic.Count > 0 || strict) ? semantic : Fuse(keywords, semantic);
        if (ranked.Count == 0) return [];
        var candidates = await db.KnowledgeChunks.AsNoTracking().Include(c => c.Document)
            .Where(c => ranked.Contains(c.Id) && c.Document != null && c.Document.Approved && c.Document.Language == "en")
            .ToDictionaryAsync(c => c.Id);
        var maximum = Math.Clamp(configuration.GetValue("Knowledge:MaxContextCharacters", 4500), 500, 16000);
        var selected = new List<KnowledgeChunk>();
        foreach (var id in ranked)
        {
            if (!candidates.TryGetValue(id, out var chunk)) continue;
            var length = chunk.Title.Length + chunk.Content.Length;
            if (length > maximum) continue;
            selected.Add(chunk);
            maximum -= length;
            if (selected.Count == topK) break;
        }
        return selected;
    }

    public static List<Guid> Fuse(params IReadOnlyList<Guid>[] rankings)
    {
        var scores = new Dictionary<Guid, double>();
        foreach (var ranking in rankings)
        {
            var rank = 0;
            foreach (var id in ranking.Distinct())
                scores[id] = scores.GetValueOrDefault(id) + 1.0 / (60 + ++rank);
        }
        return scores.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key)
            .Select(pair => pair.Key).ToList();
    }
}
