using Microsoft.EntityFrameworkCore;
using Npgsql;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Services;

namespace NutriPath.Api.Tests;

public sealed class VectorDatabaseFactAttribute : FactAttribute
{
    public VectorDatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NUTRIPATH_VECTOR_TEST_DB")))
            Skip = "Provide an isolated pgvector database using NUTRIPATH_VECTOR_TEST_DB.";
    }
}

public class KnowledgeVectorIntegrationTests
{
    [VectorDatabaseFact]
    public async Task SearchesApprovedVectorsByDistanceAndModel()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NUTRIPATH_VECTOR_TEST_DB"));
        Assert.Equal("nutripath_vector_test", connection.Database);
        var options = new DbContextOptionsBuilder<NutriPathDbContext>().UseNpgsql(connection.ConnectionString).Options;
        await using var db = new NutriPathDbContext(options);
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync(KnowledgeVectors.SetupSql);
        await using var transaction = await db.Database.BeginTransactionAsync();

        var near = AddChunk(db, approved: true);
        var far = AddChunk(db, approved: true);
        var draft = AddChunk(db, approved: false);
        var otherModel = AddChunk(db, approved: true);
        var zeroVector = AddChunk(db, approved: true);
        await db.SaveChangesAsync();

        var model = Guid.NewGuid().ToString("N");
        var query = new float[384];
        query[0] = 1;
        var distant = new float[384];
        distant[1] = 1;
        await KnowledgeVectors.PutAsync(db, near.Id, model, distant);
        await KnowledgeVectors.PutAsync(db, near.Id, model, query);
        await KnowledgeVectors.PutAsync(db, far.Id, model, distant);
        await KnowledgeVectors.PutAsync(db, draft.Id, model, query);
        await KnowledgeVectors.PutAsync(db, otherModel.Id, model + "-other", query);
        await KnowledgeVectors.PutAsync(db, zeroVector.Id, model, new float[384]);

        Assert.Equal(2, await KnowledgeVectors.CountIndexedApprovedChunksAsync(db, model));
        Assert.Equal([near.Id], await KnowledgeVectors.SearchAsync(db, query, model, 0.25, 10));
        Assert.Equal([near.Id, far.Id], await KnowledgeVectors.SearchAsync(db, query, model, 1.1, 2));
        await transaction.RollbackAsync();
    }

    private static KnowledgeChunk AddChunk(NutriPathDbContext db, bool approved)
    {
        var chunk = new KnowledgeChunk { Title = "Vector fixture", Content = "Disposable integration test." };
        db.KnowledgeDocuments.Add(new KnowledgeDocument
        {
            Approved = approved,
            ContentHash = Guid.NewGuid().ToString("N"),
            Chunks = [chunk],
        });
        return chunk;
    }
}
