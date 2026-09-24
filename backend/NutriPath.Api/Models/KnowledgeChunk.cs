namespace NutriPath.Api.Models;

/// <summary>
/// One retrievable piece of nutrition knowledge. Deliberately short
/// (a paragraph, not a whole article) — retrieval works better over
/// focused chunks than long documents, since similarity gets diluted
/// when one chunk covers too many unrelated ideas.
/// </summary>
public class KnowledgeChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
