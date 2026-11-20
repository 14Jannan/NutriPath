using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using NutriPath.Api.Data;
using NutriPath.Api.Migrations;
using NutriPath.Api.Models;
using NutriPath.Api.Services;

namespace NutriPath.Api.Tests;

public class KnowledgeTests
{
    [Theory]
    [InlineData("Stick to shrimp options that are allergy-free.")]
    [InlineData("Those snacks are allergen‑free.")]
    [InlineData("This product is guaranteed safe.")]
    [InlineData("There is no need to check the ingredients.")]
    [InlineData("There is no risk of cross-contact.")]
    [InlineData("There is zero cross‑contact.")]
    public void UnverifiedAllergyGuaranteesAreReplaced(string answer)
    {
        var reply = JsonSerializer.Serialize(new { answer, sourceIds = Array.Empty<Guid>(), insufficientEvidence = false });
        var parsed = GroundedNutritionAnswer.Parse(reply, []);
        Assert.True(parsed.InsufficientEvidence);
        Assert.Contains("cross-contact", parsed.Answer);
        Assert.DoesNotContain(answer, parsed.Answer);
    }

    [Fact]
    public void AllergyUncertaintyAndCrossContactAdviceArePreserved()
    {
        const string answer = "I can’t confirm the ingredients. Ask the kitchen about cross-contact and avoid peanuts.";
        var reply = JsonSerializer.Serialize(new { answer, sourceIds = Array.Empty<Guid>(), insufficientEvidence = true });
        var parsed = GroundedNutritionAnswer.Parse(reply, []);
        Assert.Equal(answer, parsed.Answer);
        Assert.True(parsed.InsufficientEvidence);
        Assert.Empty(parsed.References);
    }
    [Theory]
    [InlineData(0, 0)]
    [InlineData(24, 0)]
    [InlineData(24, 23)]
    public void SemanticBenchmarkRejectsMissingOrPartialCoverage(int approved, int indexed)
    {
        Assert.Throws<InvalidOperationException>(() => KnowledgeCli.EnsureBenchmarkCoverage(approved, indexed));
    }

    [Fact]
    public void SemanticBenchmarkAcceptsCompleteCoverage()
    {
        KnowledgeCli.EnsureBenchmarkCoverage(24, 24);
    }
    [Fact]
    public void MigrationRepairsLegacyTableBeforeAddingMetadata()
    {
        var migration = new AddManagedKnowledge();
        var repair = Assert.IsType<SqlOperation>(migration.UpOperations[0]).Sql;
        Assert.Contains("""ALTER TABLE " KnowledgeChunks" RENAME TO "KnowledgeChunks" """.Trim(), repair);
        Assert.Contains("""ON CONFLICT ("Id") DO NOTHING""", repair);
        Assert.DoesNotContain(migration.UpOperations, operation => operation is DropTableOperation);
    }

    [Fact]
    public async Task KeywordFallbackOnlyReturnsApprovedDocumentsEvenWhenIndexIsCached()
    {
        var dbName = Guid.NewGuid().ToString();
        using var services = new ServiceCollection()
            .AddDbContext<NutriPathDbContext>(options => options.UseInMemoryDatabase(dbName)).BuildServiceProvider();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();
        var approved = new KnowledgeDocument { Approved = true, Title = "Fibre", ContentHash = "first" };
        approved.Chunks.Add(new KnowledgeChunk { Title = "Fibre", Content = "Beans contain fibre" });
        var draft = new KnowledgeDocument { Title = "Draft", ContentHash = "second" };
        draft.Chunks.Add(new KnowledgeChunk { Title = "Draft", Content = "Beans contain fibre" });
        db.AddRange(approved, draft);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().Build();
        using var client = new HttpClient();
        var retrieval = new HybridKnowledgeRetrieval(
            new KnowledgeRetrievalService(services.GetRequiredService<IServiceScopeFactory>()), db,
            new LocalEmbeddingService(client, config), config, NullLogger<HybridKnowledgeRetrieval>.Instance);
        await retrieval.EnsureIndexBuiltAsync();
        Assert.Equal(approved.Id, Assert.Single(await retrieval.SearchAsync("fibre")).DocumentId);
        approved.Approved = false;
        await db.SaveChangesAsync();
        Assert.Empty(await retrieval.SearchAsync("fibre"));
    }

    [Fact]
    public void ChunkingPreservesHeadingsAndLongParagraphOverlap()
    {
        var output = KnowledgeCli.Split("# Protein\none two three four five six\n\n# Fibre\nnew section", 4, 1);
        Assert.Equal(["# Protein\none two three four", "# Protein\nfour five six", "# Fibre\nnew section"], output);
    }

    [Fact]
    public async Task ImportValidatesEnglishSourceMetadataAndNormalizesContentHash()
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid()}.md");
        var metadata = path + ".json";
        try
        {
            await File.WriteAllTextAsync(metadata, """
                {"title":"Fibre","category":"food","organization":"Editorial",
                 "sourceUrl":"https://example.org/reference","language":"en"}
                """);
            await File.WriteAllTextAsync(path, "# Fibre\r\n\r\nBeans have dietary fibre.");
            var first = await KnowledgeCli.ReadDocumentAsync(path);
            await File.WriteAllTextAsync(path, "# Fibre\n\nBeans  have dietary fibre.");
            var duplicate = await KnowledgeCli.ReadDocumentAsync(path);
            Assert.Equal(first.ContentHash, duplicate.ContentHash);
            Assert.Equal("# Fibre\n\nBeans have dietary fibre.", duplicate.Content);
            Assert.False(KnowledgeCli.IsSourceUrl("javascript:alert(1)"));
            Assert.False(KnowledgeCli.IsSourceUrl("https://user@example.org/"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(metadata);
        }
    }

    [Fact]
    public void RankFusionPromotesSharedItemsAndDeduplicates()
    {
        var keyword = Guid.NewGuid();
        var vector = Guid.NewGuid();
        var shared = Guid.NewGuid();
        var result = HybridKnowledgeRetrieval.Fuse([keyword, shared, shared], [vector, shared]);
        Assert.Equal(shared, result[0]);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void CitationsOnlyResolveFromRetrievedApprovedSources()
    {
        var doc = new KnowledgeDocument
        {
            Title = "Nutrition", SourceUrl = "https://example.org/nutrition", Approved = true,
        };
        var chunks = new List<KnowledgeChunk> { new() { Document = doc } };
        string Reply(Guid sourceId, string answer = "From the approved evidence") =>
            JsonSerializer.Serialize(new { answer, sourceIds = new[] { sourceId }, insufficientEvidence = false });

        Assert.Equal(doc.SourceUrl, Assert.Single(GroundedNutritionAnswer.Parse(Reply(doc.Id), chunks).References).Url);
        Assert.True(GroundedNutritionAnswer.Parse(Reply(Guid.NewGuid()), chunks).InsufficientEvidence);
        Assert.Empty(GroundedNutritionAnswer.Parse(Reply(doc.Id, "See https://untrusted.example"), chunks).References);
        Assert.True(GroundedNutritionAnswer.Parse("invalid JSON", chunks).InsufficientEvidence);
    }

    [Fact]
    public void UnsupportedNutritionQuantitiesFailClosed()
    {
        const string reply = """{"answer":"Your target is 100 g of protein.","sourceIds":[],"insufficientEvidence":false}""";
        Assert.True(GroundedNutritionAnswer.Parse(reply, [], """{"target":90}""").InsufficientEvidence);
        Assert.False(GroundedNutritionAnswer.Parse(reply, [], """{"target":100.0}""").InsufficientEvidence);
    }

    [Fact]
    public async Task LocalEmbeddingValidatesModelAndDimension()
    {
        var values = new Dictionary<string, string?> { ["Knowledge:EmbeddingUrl"] = "http://127.0.0.1:8092/embed" };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        using var handler = new ModelHandler();
        using var http = new HttpClient(handler);
        var service = new LocalEmbeddingService(http, configuration);
        handler.Model = service.Model;
        Assert.Equal(384, Assert.Single(await service.EmbedAsync(["nutrition"])).Length);
        handler.Dimension = 383;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EmbedAsync(["nutrition"]));
        handler.Dimension = 384;
        handler.Model = "another-model";
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EmbedAsync(["nutrition"]));
    }

    private sealed class ModelHandler : HttpMessageHandler
    {
        public string Model { get; set; } = "";
        public int Dimension { get; set; } = 384;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.True(request.Content?.Headers.ContentLength > 0);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { model = Model, vectors = new[] { Enumerable.Repeat(1f, Dimension).ToArray() } }),
            });
        }
    }
}
