using NutriPath.Api.Models;

namespace NutriPath.Api.Services;

public interface IKnowledgeRetrievalService
{
    Task EnsureIndexBuiltAsync();
    List<KnowledgeChunk> Search(string query, int topK);
}
