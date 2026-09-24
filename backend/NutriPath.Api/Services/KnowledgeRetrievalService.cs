using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.Models;

namespace NutriPath.Api.Services;

/// <summary>
/// A minimal TF-IDF + cosine similarity search engine, built entirely
/// in-process — no external embeddings API, no vector database. This is
/// appropriate for a small, curated knowledge base (tens to low hundreds
/// of documents); a larger system would move to neural embeddings + a
/// proper vector index, but the RETRIEVAL PATTERN (vectorize, compare,
/// take top-K) stays identical — only Vectorize would change.
///
/// Registered as a Singleton because the index is expensive to build but
/// cheap to reuse, and the knowledge base rarely changes.
/// </summary>
public class KnowledgeRetrievalService : IKnowledgeRetrievalService
{
    private static readonly HashSet<string> Stopwords = new()
    {
        "the", "a", "an", "is", "are", "of", "and", "to", "in", "for", "on", "with", "or",
        "what", "why", "how", "should", "can", "my", "me", "about", "this", "that", "today",
    };

    private readonly IServiceScopeFactory _scopeFactory;

    // Guards the one-time build: two first requests arriving together
    // must not both build (and half-overwrite) the index.
    private readonly SemaphoreSlim _buildLock = new(1, 1);
    private volatile bool _isBuilt;

    private List<KnowledgeChunk> _chunks = new();
    private List<Dictionary<string, double>> _chunkVectors = new();
    private Dictionary<string, double> _idf = new();

    public KnowledgeRetrievalService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task EnsureIndexBuiltAsync()
    {
        if (_isBuilt) return;

        await _buildLock.WaitAsync();
        try
        {
            if (_isBuilt) return;

            // A Singleton can't directly depend on the Scoped DbContext, so
            // we create a short-lived scope just to load the data once.
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();

            _chunks = await db.KnowledgeChunks.AsNoTracking().ToListAsync();
            BuildTfIdfIndex();
            _isBuilt = true;
        }
        finally
        {
            _buildLock.Release();
        }
    }

    public List<KnowledgeChunk> Search(string query, int topK)
    {
        if (!_isBuilt || _chunks.Count == 0) return new List<KnowledgeChunk>();

        var queryVector = Vectorize(Tokenize(query));

        return _chunks
            .Select((chunk, i) => (chunk, score: CosineSimilarity(queryVector, _chunkVectors[i])))
            .Where(s => s.score > 0) // don't return completely unrelated chunks
            .OrderByDescending(s => s.score)
            .Take(topK)
            .Select(s => s.chunk)
            .ToList();
    }

    private void BuildTfIdfIndex()
    {
        var tokenizedDocs = _chunks.Select(c => Tokenize($"{c.Title} {c.Content}")).ToList();

        // Document frequency: how many documents contain each word at all.
        var documentFrequency = new Dictionary<string, int>();
        foreach (var doc in tokenizedDocs)
        {
            foreach (var term in doc.Distinct())
            {
                documentFrequency[term] = documentFrequency.GetValueOrDefault(term) + 1;
            }
        }

        // IDF: rare words get a HIGH weight since they distinguish a
        // document; words in almost every document get a LOW weight.
        var totalDocs = tokenizedDocs.Count;
        _idf = documentFrequency.ToDictionary(
            kv => kv.Key,
            kv => Math.Log((double)totalDocs / kv.Value));

        _chunkVectors = tokenizedDocs.Select(Vectorize).ToList();
    }

    private Dictionary<string, double> Vectorize(List<string> tokens)
    {
        if (tokens.Count == 0) return new Dictionary<string, double>();

        // TF: how often each word appears in THIS document, normalized by
        // length so a long document doesn't score higher just for repeating words.
        var termFrequency = new Dictionary<string, double>();
        foreach (var term in tokens)
        {
            termFrequency[term] = termFrequency.GetValueOrDefault(term) + 1;
        }

        var vector = new Dictionary<string, double>();
        foreach (var (term, count) in termFrequency)
        {
            var tf = count / tokens.Count;
            var idf = _idf.GetValueOrDefault(term, 0); // unseen word → contributes nothing
            vector[term] = tf * idf;
        }
        return vector;
    }

    private static List<string> Tokenize(string text)
    {
        var words = Regex.Matches(text.ToLowerInvariant(), "[a-z]+").Select(m => m.Value);
        return words.Where(w => w.Length > 2 && !Stopwords.Contains(w)).ToList();
    }

    // Cosine similarity compares the ANGLE between two vectors, not their
    // distance — so a short and a long document using the same important
    // words in the same proportions still score as very similar.
    private static double CosineSimilarity(Dictionary<string, double> a, Dictionary<string, double> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;

        // Any term not in both vectors contributes zero to the dot product.
        double dotProduct = 0;
        foreach (var (term, valueA) in a)
        {
            if (b.TryGetValue(term, out var valueB))
            {
                dotProduct += valueA * valueB;
            }
        }

        var magnitudeA = Math.Sqrt(a.Values.Sum(v => v * v));
        var magnitudeB = Math.Sqrt(b.Values.Sum(v => v * v));
        if (magnitudeA == 0 || magnitudeB == 0) return 0;

        return dotProduct / (magnitudeA * magnitudeB);
    }
}
