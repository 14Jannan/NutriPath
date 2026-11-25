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
        SearchWithModeAsync(query, configuration["Knowledge:RetrievalMode"] ?? "semantic");

    public async Task<List<KnowledgeChunk>> SearchWithModeAsync(string query, string mode, bool strict = false,
        int? evaluationTopK = null)
    {
        var topK = Math.Clamp(evaluationTopK ?? configuration.GetValue("Knowledge:TopK", 3), 1, 12);
        var count = Math.Clamp(configuration.GetValue("Knowledge:CandidateCount", 12), topK, 50);
        var minimumKeyword = Math.Clamp(configuration.GetValue("Knowledge:MinimumKeywordSimilarity", 0d), 0, 1);
        // The keyword index is English. Tamil, Sinhala and transliterated questions are
        // expanded with reviewed English nutrition terms; without a mapped term they simply
        // find no keyword evidence rather than a misleading match.
        var questionLanguage = Multilingual.LanguageResolver.Detect(query).Language ?? Multilingual.Languages.English;
        var keywordQuery = Multilingual.NutritionTermLexicon.ExpandForKeywordSearch(query, questionLanguage);
        var keywords = keyword.Search(keywordQuery, count, minimumKeyword).Select(c => c.Id).ToList();
        List<Guid>? semantic = null;
        if (mode != "keyword" && embeddings.Enabled && db.Database.IsNpgsql())
        {
            try
            {
                // A model with no stored vectors was never reindexed. Searching it would return
                // nothing and look like "no evidence", so treat the channel as unavailable.
                if (!strict && await VectorCountAsync(embeddings.Model) == 0)
                    throw new InvalidOperationException("No vectors are indexed for the configured embedding model.");
                var vectors = await embeddings.EmbedAsync([SemanticQuery(query, questionLanguage)], EmbeddingInputType.Query);
                semantic = await KnowledgeVectors.SearchAsync(db, vectors[0], embeddings.Model,
                    EmbeddingModels.MaxDistance(configuration, embeddings.Model,
                        crossLingual: questionLanguage != Multilingual.Languages.English), count);
            }
            catch (Exception ex) when (!strict && ex is (HttpRequestException or OperationCanceledException or
                InvalidOperationException or NpgsqlException or System.Text.Json.JsonException or FormatException))
            {
                logger.LogWarning("Semantic nutrition retrieval unavailable ({ErrorType}); using keyword search.", ex.GetType().Name);
            }
        }

        // An empty successful semantic search is an evidence decision. Only
        // fall back to keywords when the semantic channel was unavailable.
        var ranked = mode == "semantic" && (semantic is not null || strict)
            ? semantic ?? []
            : Fuse(keywords, semantic ?? []);
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

    /// <summary>
    /// The text embedded for semantic search. The multilingual model understands Tamil and
    /// Sinhala script but not Tamil or Sinhala typed in English letters (Tanglish, Singlish),
    /// so a romanised question is embedded together with its mapped English nutrition terms.
    /// Script and English questions are embedded unchanged.
    /// </summary>
    public string SemanticQuery(string query, string questionLanguage)
    {
        if (questionLanguage == Multilingual.Languages.English || !Multilingual.Transliteration.IsRomanised(query) ||
            !configuration.GetValue("Knowledge:TransliterationQueryExpansion", true))
            return query;
        var terms = Multilingual.NutritionTermLexicon.EnglishTermsFor(query);
        return terms.Count == 0 ? query : $"{query} ({string.Join(", ", terms)})";
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Count, DateTime Until)>
        VectorCounts = new();

    private async Task<int> VectorCountAsync(string model)
    {
        if (VectorCounts.TryGetValue(model, out var cached) && cached.Until > DateTime.UtcNow) return cached.Count;
        var count = await KnowledgeVectors.CountVectorsForModelAsync(db, model);
        VectorCounts[model] = (count, DateTime.UtcNow.AddSeconds(60));
        return count;
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
