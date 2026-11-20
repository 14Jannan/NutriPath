using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;

namespace NutriPath.Api.Services;

public interface IEmbeddingService
{
    string Model { get; }
    bool Enabled { get; }
    Task<float[][]> EmbedAsync(string[] texts, CancellationToken cancellationToken = default);
}

public sealed class LocalEmbeddingService(HttpClient client, IConfiguration configuration) : IEmbeddingService
{
    private const string DefaultModel = "sentence-transformers/all-MiniLM-L6-v2";
    public string Model => configuration["Knowledge:EmbeddingModel"] ?? DefaultModel;
    public bool Enabled => !string.IsNullOrWhiteSpace(configuration["Knowledge:EmbeddingUrl"]);

    public async Task<float[][]> EmbedAsync(string[] texts, CancellationToken cancellationToken = default)
    {
        var url = new Uri(configuration["Knowledge:EmbeddingUrl"]!);
        if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Configure a private HTTP(S) embedding endpoint.");
        using var response = await client.PostAsJsonAsync(url, new { model = Model, texts }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken);
        if (result?.Model != Model || result.Vectors is not { } vectors || vectors.Length != texts.Length ||
            vectors.Any(v => v is not { Length: 384 } || v.Any(x => !float.IsFinite(x)) || v.All(x => x == 0)))
            throw new InvalidOperationException("The embedding response has an incompatible model or vector.");
        return vectors;
    }

    private sealed record EmbeddingResponse(string Model, float[][] Vectors);
}

public static class KnowledgeVectors
{
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
