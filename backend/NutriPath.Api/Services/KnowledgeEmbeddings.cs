using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;

namespace NutriPath.Api.Services;

/// <summary>
/// Whether text is a user question or a knowledge passage. E5 models need different
/// prefixes for each ("query: " / "passage: "); the embedding worker applies them.
/// </summary>
public enum EmbeddingInputType { Query, Passage }

public interface IEmbeddingService
{
    string Model { get; }
    bool Enabled { get; }

    /// <summary>Legacy overload: embeds knowledge passages.</summary>
    Task<float[][]> EmbedAsync(string[] texts, CancellationToken cancellationToken = default);

    Task<float[][]> EmbedAsync(string[] texts, EmbeddingInputType inputType, CancellationToken cancellationToken = default) =>
        EmbedAsync(texts, cancellationToken);
}

/// <summary>Embedding models this app knows how to use, and their retrieval defaults.</summary>
public static class EmbeddingModels
{
    public const string MultilingualE5Small = "intfloat/multilingual-e5-small";
    public const string MiniLm = "sentence-transformers/all-MiniLM-L6-v2";
    public const int Dimensions = 384; // the KnowledgeEmbeddings column is vector(384)

    /// <summary>
    /// Default cosine-distance cut-off for a model. E5 places all text in a narrow cone, so its
    /// cut-offs are much tighter than MiniLM's, and a Tamil or Sinhala question sits measurably
    /// further from the English passages than an English question does. The E5 values come
    /// from the threshold sweeps in docs/multilingual-ai.md: on the 104-question English benchmark
    /// semantic recall@3 climbs from 0.844 (0.16) to 0.948 (0.18) and flattens after, while more
    /// negatives receive evidence; Tamil and Sinhala recall keeps rising until 0.24, but negatives
    /// start receiving evidence beyond 0.22.
    /// </summary>
    public static double DefaultMaxDistance(string model, bool crossLingual = false) => model switch
    {
        MultilingualE5Small => crossLingual ? 0.22 : 0.18,
        _ => 0.65,
    };

    /// <summary>
    /// Knowledge:MaxVectorDistance overrides the cut-off for English questions and
    /// Knowledge:CrossLingualMaxVectorDistance the one for Tamil, Sinhala and transliterated questions.
    /// </summary>
    public static double MaxDistance(IConfiguration configuration, string model, bool crossLingual = false)
    {
        var configured = crossLingual
            ? configuration.GetValue<double?>("Knowledge:CrossLingualMaxVectorDistance")
              ?? (model == MultilingualE5Small ? null : configuration.GetValue<double?>("Knowledge:MaxVectorDistance"))
            : configuration.GetValue<double?>("Knowledge:MaxVectorDistance");
        return Math.Clamp(configured ?? DefaultMaxDistance(model, crossLingual), 0, 2);
    }
}

public sealed class LocalEmbeddingService(HttpClient client, IConfiguration configuration) : IEmbeddingService
{
    // Kept as the default so existing deployments keep comparing like with like until the
    // multilingual model has been reindexed and validated, then selected in configuration.
    private const string DefaultModel = EmbeddingModels.MiniLm;
    private const double NormTolerance = 1e-3;

    // Tamil and Sinhala are sent as UTF-8 rather than \uXXXX escapes (6x smaller).
    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
    };

    public string Model => configuration["Knowledge:EmbeddingModel"] ?? DefaultModel;
    public bool Enabled => !string.IsNullOrWhiteSpace(configuration["Knowledge:EmbeddingUrl"]);
    private int Dimensions => configuration.GetValue("Knowledge:EmbeddingDimensions", EmbeddingModels.Dimensions);

    public Task<float[][]> EmbedAsync(string[] texts, CancellationToken cancellationToken = default) =>
        EmbedAsync(texts, EmbeddingInputType.Passage, cancellationToken);

    public async Task<float[][]> EmbedAsync(string[] texts, EmbeddingInputType inputType,
        CancellationToken cancellationToken = default)
    {
        if (Dimensions != EmbeddingModels.Dimensions)
            throw new InvalidOperationException("Knowledge:EmbeddingDimensions must match the vector(384) storage column.");
        var url = new Uri(configuration["Knowledge:EmbeddingUrl"]!);
        if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Configure a private HTTP(S) embedding endpoint.");
        var type = inputType == EmbeddingInputType.Query ? "query" : "passage";
        // The local worker requires a bounded Content-Length before reading the body.
        using var body = new StringContent(JsonSerializer.Serialize(new { model = Model, inputType = type, texts }, Json),
            Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(url, body, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken);
        if (result?.Model != Model || (result.InputType is { } echoed && echoed != type) ||
            result.Vectors is not { } vectors || vectors.Length != texts.Length ||
            vectors.Any(v => !Valid(v, Dimensions)))
            throw new InvalidOperationException("The embedding response has an incompatible model or vector.");
        return vectors;
    }

    /// <summary>Right size, finite, and unit length (cosine search assumes normalized vectors).</summary>
    public static bool Valid(float[]? vector, int dimensions = EmbeddingModels.Dimensions)
    {
        if (vector is null || vector.Length != dimensions) return false;
        double sum = 0;
        foreach (var x in vector)
        {
            if (!float.IsFinite(x)) return false;
            sum += (double)x * x;
        }
        return Math.Abs(Math.Sqrt(sum) - 1) <= NormTolerance;
    }

    private sealed record EmbeddingResponse(string Model, float[][] Vectors, string? InputType = null);
}

public static class KnowledgeVectors
{
    /// <summary>How many stored vectors exist for a model (0 when the table doesn't exist).</summary>
    public static async Task<int> CountVectorsForModelAsync(NutriPathDbContext db, string model)
    {
        var ready = await db.Database.SqlQuery<bool>(
            $"""SELECT to_regclass('"KnowledgeEmbeddings"') IS NOT NULL AS "Value" """).SingleAsync();
        if (!ready) return 0;
        return await db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM "KnowledgeEmbeddings" WHERE "Model" = {model}
            """).SingleAsync();
    }

    /// <summary>Stored vectors for this model whose length is not 1 (within tolerance); should always be 0.</summary>
    public static Task<int> CountUnnormalizedAsync(NutriPathDbContext db, string model) =>
        db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM "KnowledgeEmbeddings"
            WHERE "Model" = {model} AND abs(sqrt(-("Embedding" <#> "Embedding")) - 1) > 0.001
            """).SingleAsync();

    public static Task<int> CountIndexedApprovedChunksAsync(NutriPathDbContext db, string model) =>
        db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM "KnowledgeEmbeddings" e
            JOIN "KnowledgeChunks" c ON c."Id" = e."ChunkId"
            JOIN "KnowledgeDocuments" d ON d."Id" = c."DocumentId"
            WHERE d."Approved" AND d."Language" = 'en' AND e."Model" = {model}
              AND (e."Embedding" <#> e."Embedding") < 0
            """).SingleAsync();

    // Kept optional so installations without the extension can apply all EF
    // migrations and run the keyword path.
    public const string SetupSql = """
        CREATE EXTENSION IF NOT EXISTS vector;
        CREATE TABLE IF NOT EXISTS "KnowledgeEmbeddings" (
            "ChunkId" uuid REFERENCES "KnowledgeChunks" ("Id") ON DELETE CASCADE,
            "Model" text NOT NULL,
            "Embedding" vector(384) NOT NULL,
            PRIMARY KEY ("ChunkId", "Model")
        );
        """;

    public static string Literal(float[] vector) =>
        "[" + string.Join(",", vector.Select(value => value.ToString("R", CultureInfo.InvariantCulture))) + "]";

    public static async Task PutAsync(NutriPathDbContext db, Guid id, string model, float[] vector)
    {
        var value = Literal(vector);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "KnowledgeEmbeddings" ("ChunkId", "Model", "Embedding")
            VALUES ({id}, {model}, CAST({value} AS vector))
            ON CONFLICT ("ChunkId", "Model") DO UPDATE SET "Embedding" = EXCLUDED."Embedding"
            """);
    }

    /// <summary>Nearest approved chunks with their cosine distance, for threshold evaluation.</summary>
    public static Task<List<ChunkDistance>> NearestWithDistanceAsync(NutriPathDbContext db, float[] vector, string model,
        int count) =>
        db.Database.SqlQuery<ChunkDistance>($"""
            SELECT e."ChunkId" AS "ChunkId", (e."Embedding" <=> CAST({Literal(vector)} AS vector)) AS "Distance"
            FROM "KnowledgeEmbeddings" e
            JOIN "KnowledgeChunks" c ON c."Id" = e."ChunkId"
            JOIN "KnowledgeDocuments" d ON d."Id" = c."DocumentId"
            WHERE d."Approved" AND d."Language" = 'en' AND e."Model" = {model}
            ORDER BY e."Embedding" <=> CAST({Literal(vector)} AS vector), e."ChunkId"
            LIMIT {count}
            """).ToListAsync();

    public sealed class ChunkDistance
    {
        public Guid ChunkId { get; set; }
        public double Distance { get; set; }
    }

    public static Task<List<Guid>> SearchAsync(NutriPathDbContext db, float[] vector, string model,
        double maxDistance, int count) =>
        db.Database.SqlQuery<Guid>($"""
            SELECT e."ChunkId" AS "Value" FROM "KnowledgeEmbeddings" e
            JOIN "KnowledgeChunks" c ON c."Id" = e."ChunkId"
            JOIN "KnowledgeDocuments" d ON d."Id" = c."DocumentId"
            WHERE d."Approved" AND d."Language" = 'en' AND e."Model" = {model}
              AND (e."Embedding" <=> CAST({Literal(vector)} AS vector)) <= {maxDistance}
            ORDER BY e."Embedding" <=> CAST({Literal(vector)} AS vector), e."ChunkId"
            LIMIT {count}
            """).ToListAsync();
}
