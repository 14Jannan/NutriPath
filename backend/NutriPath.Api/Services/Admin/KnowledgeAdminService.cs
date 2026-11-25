using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Models.Admin;

namespace NutriPath.Api.Services.Admin;

public sealed record KnowledgeInput(string? Title, string? Category, string? Organization, string? SourceUrl,
    DateOnly? PublicationDate, string? Content);

/// <summary>
/// The knowledge review workflow on top of the CLI's validation, chunking and indexing (no second
/// implementation). Draft → PendingReview → Approved (→ indexed by a background job), or Rejected
/// / Archived. Only Approved documents are ever retrieved. Editing an approved document creates a
/// new draft version; approving it archives the old version, which is kept so earlier answers'
/// citations still resolve. Imported text is treated as untrusted data: it is stored and shown,
/// never executed or followed.
/// </summary>
public sealed class KnowledgeAdminService(NutriPathDbContext db, IConfiguration configuration, AdminAudit audit,
    BackgroundJobs jobs, IEmbeddingService embeddings)
{
    public const int MaxImportBytes = 256 * 1024;
    public const int MaxPdfBytes = 5 * 1024 * 1024;
    public const int MaxPdfPages = 100;

    public KnowledgeDocument Validate(KnowledgeInput input) =>
        KnowledgeCli.Validate(new KnowledgeCli.ImportDocument(input.Title, input.Category, input.Organization,
            input.SourceUrl, "en", input.PublicationDate, input.Content), input.Content);

    public List<string> PreviewChunks(string content) =>
        KnowledgeCli.Split(content, Math.Clamp(configuration.GetValue("Knowledge:ChunkWords", 120), 32, 180),
            Math.Clamp(configuration.GetValue("Knowledge:OverlapWords", 16), 0, 24));

    public async Task<KnowledgeDocument> CreateDraftAsync(KnowledgeInput input, Guid actor, Guid? supersedes, CancellationToken ct)
    {
        KnowledgeDocument valid;
        try
        {
            valid = Validate(input);
        }
        catch (ArgumentException ex)
        {
            throw new AdminRuleException(ex.Message, StatusCodes.Status400BadRequest);
        }
        var duplicate = await db.KnowledgeDocuments.AsNoTracking()
            .Where(d => d.ContentHash == valid.ContentHash).Select(d => new { d.Id, d.Title, d.Status }).FirstOrDefaultAsync(ct);
        if (duplicate is not null)
            throw new AdminRuleException($"The same content already exists in \"{duplicate.Title}\" ({duplicate.Status}).");

        var version = 1;
        if (supersedes is { } previousId)
        {
            var previous = await db.KnowledgeDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == previousId, ct)
                ?? throw new KeyNotFoundException();
            version = previous.Version + 1;
        }
        var chunks = PreviewChunks(valid.Content);
        var document = new KnowledgeDocument
        {
            Title = valid.Title, Category = valid.Category, Organization = valid.Organization, SourceUrl = valid.SourceUrl,
            PublicationDate = valid.PublicationDate, Content = valid.Content, ContentHash = valid.ContentHash, Language = "en",
            Status = KnowledgeStatus.Draft, Approved = false, Version = version, SupersedesDocumentId = supersedes,
            CreatedByUserId = actor, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
            Chunks = chunks.Select((c, i) => new KnowledgeChunk
            {
                Title = chunks.Count == 1 ? valid.Title : $"{valid.Title} ({i + 1})", Content = c, Category = valid.Category,
            }).ToList(),
        };
        db.KnowledgeDocuments.Add(document);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("knowledge.create", resourceType: "KnowledgeDocument", resourceId: document.Id.ToString(),
            summary: $"\"{document.Title}\" v{version} ({chunks.Count} chunks){(supersedes is null ? "" : $", revises {supersedes}")}", cancellationToken: ct);
        return document;
    }

    /// <summary>Drafts and rejected documents are edited in place; approved ones get a new draft version.</summary>
    public async Task<KnowledgeDocument> EditAsync(Guid id, KnowledgeInput input, Guid actor, CancellationToken ct)
    {
        var document = await db.KnowledgeDocuments.Include(d => d.Chunks).SingleOrDefaultAsync(d => d.Id == id, ct) ?? throw new KeyNotFoundException();
        if (document.Status is KnowledgeStatus.Approved or KnowledgeStatus.Archived)
            return await CreateDraftAsync(input, actor, document.Id, ct);
        if (document.Status == KnowledgeStatus.PendingReview)
            throw new AdminRuleException("This document is waiting for review. Withdraw or reject it before editing.");

        KnowledgeDocument valid;
        try
        {
            valid = Validate(input);
        }
        catch (ArgumentException ex)
        {
            throw new AdminRuleException(ex.Message, StatusCodes.Status400BadRequest);
        }
        if (await db.KnowledgeDocuments.AnyAsync(d => d.ContentHash == valid.ContentHash && d.Id != id, ct))
            throw new AdminRuleException("The same content already exists in another document.");
        var chunks = PreviewChunks(valid.Content);
        db.KnowledgeChunks.RemoveRange(document.Chunks);
        document.Title = valid.Title;
        document.Category = valid.Category;
        document.Organization = valid.Organization;
        document.SourceUrl = valid.SourceUrl;
        document.PublicationDate = valid.PublicationDate;
        document.Content = valid.Content;
        document.ContentHash = valid.ContentHash;
        document.Status = KnowledgeStatus.Draft;
        document.RejectionReason = null;
        document.UpdatedAtUtc = DateTime.UtcNow;
        document.Chunks = chunks.Select((c, i) => new KnowledgeChunk
        {
            Title = chunks.Count == 1 ? valid.Title : $"{valid.Title} ({i + 1})", Content = c, Category = valid.Category, DocumentId = id,
        }).ToList();
        db.KnowledgeChunks.AddRange(document.Chunks);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("knowledge.edit", resourceType: "KnowledgeDocument", resourceId: id.ToString(), summary: $"{chunks.Count} chunks", cancellationToken: ct);
        return document;
    }

    public async Task SubmitAsync(Guid id, CancellationToken ct)
    {
        var document = await Find(id, ct);
        if (document.Status is not (KnowledgeStatus.Draft or KnowledgeStatus.Rejected))
            throw new AdminRuleException("Only drafts and rejected documents can be submitted for review.");
        document.Status = KnowledgeStatus.PendingReview;
        document.UpdatedAtUtc = DateTime.UtcNow;
        db.AdminNotifications.Add(new AdminNotification
        {
            Type = "knowledge.submitted", Title = $"Knowledge waiting for review: {document.Title}",
            Permission = Permissions.KnowledgeReview, Link = $"/knowledge/{id}",
        });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("knowledge.submit", resourceType: "KnowledgeDocument", resourceId: id.ToString(), cancellationToken: ct);
    }

    /// <summary>
    /// Approves a pending document. The reviewer must not be its author (independent review).
    /// Queues embedding of the new chunks when vector search is configured.
    /// </summary>
    public async Task<Guid?> ApproveAsync(Guid id, Guid reviewer, CancellationToken ct)
    {
        var document = await Find(id, ct);
        if (document.Status != KnowledgeStatus.PendingReview)
            throw new AdminRuleException("Only documents waiting for review can be approved.");
        if (document.CreatedByUserId == reviewer)
            throw new AdminRuleException("Another reviewer must approve documents you wrote.", StatusCodes.Status403Forbidden);
        document.Status = KnowledgeStatus.Approved;
        document.Approved = true;
        document.ReviewedByUserId = reviewer;
        document.ReviewedAtUtc = document.UpdatedAtUtc = DateTime.UtcNow;
        if (document.SupersedesDocumentId is { } old &&
            await db.KnowledgeDocuments.SingleOrDefaultAsync(d => d.Id == old, ct) is { } previous && previous.Status == KnowledgeStatus.Approved)
        {
            previous.Status = KnowledgeStatus.Archived;
            previous.Approved = false;
            previous.UpdatedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        Guid? job = null;
        if (embeddings.Enabled)
            job = await jobs.EnqueueAsync(BackgroundJobs.KnowledgeIndex, new { documentId = id }, reviewer, ct);
        await audit.WriteAsync("knowledge.approve", resourceType: "KnowledgeDocument", resourceId: id.ToString(),
            summary: $"\"{document.Title}\" v{document.Version} approved{(job is null ? " (vector indexing not configured)" : "; indexing queued")}", cancellationToken: ct);
        return job;
    }

    public async Task RejectAsync(Guid id, string reason, Guid reviewer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new AdminRuleException("Give a reason for the author.", StatusCodes.Status400BadRequest);
        var document = await Find(id, ct);
        if (document.Status != KnowledgeStatus.PendingReview)
            throw new AdminRuleException("Only documents waiting for review can be rejected.");
        document.Status = KnowledgeStatus.Rejected;
        document.RejectionReason = reason.Trim()[..Math.Min(500, reason.Trim().Length)];
        document.ReviewedByUserId = reviewer;
        document.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("knowledge.reject", resourceType: "KnowledgeDocument", resourceId: id.ToString(), reason: reason, cancellationToken: ct);
    }

    /// <summary>Removes an approved document from retrieval. The row stays for citation history.</summary>
    public async Task ArchiveAsync(Guid id, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new AdminRuleException("Give a reason.", StatusCodes.Status400BadRequest);
        var document = await Find(id, ct);
        if (document.Status == KnowledgeStatus.Archived) return;
        document.Status = KnowledgeStatus.Archived;
        document.Approved = false;
        document.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("knowledge.archive", resourceType: "KnowledgeDocument", resourceId: id.ToString(), reason: reason, cancellationToken: ct);
    }

    /// <summary>Starts a new draft from an archived or rejected version's content.</summary>
    public async Task<KnowledgeDocument> RestoreAsDraftAsync(Guid id, Guid actor, CancellationToken ct)
    {
        var source = await Find(id, ct);
        if (source.Status is not (KnowledgeStatus.Archived or KnowledgeStatus.Rejected))
            throw new AdminRuleException("Only archived or rejected versions can be restored as a draft.");
        // The content hash must be unique, so a restored copy is marked as a new revision.
        var input = new KnowledgeInput(source.Title, source.Category, source.Organization, source.SourceUrl, source.PublicationDate,
            source.Content + $"\n\n(Restored from version {source.Version} on {DateTime.UtcNow:yyyy-MM-dd}.)");
        return await CreateDraftAsync(input, actor, source.Id, ct);
    }

    /// <summary>Parses an uploaded TXT, Markdown or JSON file into draft fields (not saved).</summary>
    public static KnowledgeInput ParseUpload(string fileName, byte[] bytes, KnowledgeInput metadata)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension == ".pdf") return metadata with { Content = ExtractPdfText(bytes) };
        if (bytes.Length == 0 || bytes.Length > MaxImportBytes) throw new AdminRuleException("Files must be 1 byte to 256 KB.", StatusCodes.Status400BadRequest);
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            throw new AdminRuleException("The file is not UTF-8 text.", StatusCodes.Status400BadRequest);
        }
        switch (extension)
        {
            case ".txt" or ".md":
                return metadata with { Content = text };
            case ".json":
                try
                {
                    var doc = JsonSerializer.Deserialize<KnowledgeCli.ImportDocument>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                        ?? throw new AdminRuleException("The JSON file is empty.", StatusCodes.Status400BadRequest);
                    return new KnowledgeInput(doc.Title ?? metadata.Title, doc.Category ?? metadata.Category,
                        doc.Organization ?? metadata.Organization, doc.SourceUrl ?? metadata.SourceUrl,
                        doc.PublicationDate ?? metadata.PublicationDate, doc.Content);
                }
                catch (JsonException)
                {
                    throw new AdminRuleException("The JSON file is not valid.", StatusCodes.Status400BadRequest);
                }
            default:
                throw new AdminRuleException("Supported formats: .txt, .md, .json and .pdf.", StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>
    /// Extracts the text layer of a PDF (PdfPig, reading order). Scanned PDFs without a text layer
    /// are rejected rather than guessed at: there is no OCR. The reviewer always checks the result.
    /// </summary>
    public static string ExtractPdfText(byte[] bytes)
    {
        if (bytes.Length is 0 or > MaxPdfBytes) throw new AdminRuleException("PDF files must be at most 5 MB.", StatusCodes.Status400BadRequest);
        if (bytes.Length < 5 || Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-") throw new AdminRuleException("The file is not a PDF.", StatusCodes.Status400BadRequest);
        try
        {
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);
            if (pdf.NumberOfPages > MaxPdfPages) throw new AdminRuleException($"PDFs can have at most {MaxPdfPages} pages.", StatusCodes.Status400BadRequest);
            var pages = pdf.GetPages()
                .Select(page => UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page))
                .Select(t => System.Text.RegularExpressions.Regex.Replace(t, @"[ \t]+", " ").Trim())
                .Where(t => t.Length > 0);
            var text = string.Join("\n\n", pages);
            if (text.Length < 50) throw new AdminRuleException("No text was found in this PDF (scanned PDFs aren't supported).", StatusCodes.Status400BadRequest);
            return text.Length > 200_000 ? text[..200_000] : text;
        }
        catch (Exception ex) when (ex is not AdminRuleException)
        {
            throw new AdminRuleException(ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
                ? "Password-protected PDFs aren't supported." : "The PDF could not be read.", StatusCodes.Status400BadRequest);
        }
    }

    private async Task<KnowledgeDocument> Find(Guid id, CancellationToken ct) =>
        await db.KnowledgeDocuments.SingleOrDefaultAsync(d => d.Id == id, ct) ?? throw new KeyNotFoundException();
}
