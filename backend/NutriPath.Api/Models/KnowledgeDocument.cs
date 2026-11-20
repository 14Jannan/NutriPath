namespace NutriPath.Api.Models;

public sealed class KnowledgeDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string Category { get; set; } = "";
    public string Organization { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string Language { get; set; } = "en";
    public string ContentHash { get; set; } = "";
    public DateOnly? PublicationDate { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public bool Approved { get; set; }
    public List<KnowledgeChunk> Chunks { get; set; } = [];
}
