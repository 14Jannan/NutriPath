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
    // Retrieval only ever uses Approved documents. Status adds the review workflow around it
    // (Draft → PendingReview → Approved, or Rejected / Archived) and is kept in step with Approved.
    public bool Approved { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(20)]
    public string Status { get; set; } = KnowledgeStatus.Draft;
    public int Version { get; set; } = 1;
    public Guid? CreatedByUserId { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(500)]
    public string? RejectionReason { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    // Set on a draft created by editing an approved document. When the draft is approved the
    // superseded version is archived (kept, so earlier answers' citations stay valid).
    public Guid? SupersedesDocumentId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<KnowledgeChunk> Chunks { get; set; } = [];
}
