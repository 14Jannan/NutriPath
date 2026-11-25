using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Authorization;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Models.Admin;
using NutriPath.Api.Services;
using NutriPath.Api.Services.Admin;

namespace NutriPath.Api.Controllers.Admin;

public sealed record ReindexRequest(Guid? DocumentId);

/// <summary>RAG knowledge: drafts, review, approval, archival, chunks and embedding coverage.</summary>
[Route("api/admin/knowledge")]
public sealed class AdminKnowledgeController(NutriPathDbContext db, KnowledgeAdminService knowledge, BackgroundJobs jobs,
    IEmbeddingService embeddings, IConfiguration configuration, IHttpClientFactory http) : AdminControllerBase
{
    [HttpGet("documents")]
    [AdminPermission(Permissions.KnowledgeRead)]
    public async Task<IActionResult> List(string? status, string? category, string? search, int page = 1, int pageSize = 25,
        CancellationToken ct = default)
    {
        (page, pageSize) = Paging(page, pageSize);
        var query = db.KnowledgeDocuments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(d => d.Status == status);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(d => d.Category == category);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(d => EF.Functions.ILike(d.Title, SearchPatterns.Contains(search.Trim()), SearchPatterns.EscapeCharacter));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(d => d.UpdatedAtUtc ?? d.ReviewedAtUtc).ThenBy(d => d.Title)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(d => new
            {
                d.Id, d.Title, d.Category, d.Organization, d.SourceUrl, d.Language, d.PublicationDate, d.Version, d.Status,
                chunks = d.Chunks.Count, d.UpdatedAtUtc, d.ReviewedAtUtc, d.ReviewedByUserId, d.CreatedByUserId, d.SupersedesDocumentId,
                chunkIds = d.Chunks.Select(c => c.Id).ToList(),
            }).ToListAsync(ct);
        var indexed = await IndexedChunksAsync(rows.SelectMany(r => r.chunkIds).ToList(), ct);
        var people = await EmailsAsync(rows.SelectMany(r => new[] { r.ReviewedByUserId, r.CreatedByUserId }), ct);
        var categories = await db.KnowledgeDocuments.AsNoTracking().Select(d => d.Category).Distinct().OrderBy(c => c).ToListAsync(ct);
        return Ok(new
        {
            total, page, pageSize, categories,
            items = rows.Select(r => new
            {
                r.Id, r.Title, r.Category, r.Organization, r.SourceUrl, r.Language, r.PublicationDate, r.Version, r.Status, r.chunks,
                r.UpdatedAtUtc, r.ReviewedAtUtc, r.SupersedesDocumentId,
                reviewer = r.ReviewedByUserId is { } rv ? people.GetValueOrDefault(rv) : null,
                author = r.CreatedByUserId is { } au ? people.GetValueOrDefault(au) : "Seed/CLI",
                embedding = indexed is null ? "Unavailable"
                    : r.Status != KnowledgeStatus.Approved ? "NotApplicable"
                    : r.chunkIds.Count(indexed.Contains) == r.chunks ? "Indexed"
                    : r.chunkIds.Any(indexed.Contains) ? "Partial" : "Missing",
            }),
        });
    }

    [HttpGet("documents/{id:guid}")]
    [AdminPermission(Permissions.KnowledgeRead)]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var d = await db.KnowledgeDocuments.AsNoTracking().Include(x => x.Chunks).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound();
        // Version chain: walk back through superseded versions and forward through revisions.
        var history = new List<object>();
        var cursor = d.SupersedesDocumentId;
        for (var i = 0; cursor is { } c && i < 20; i++)
        {
            var older = await db.KnowledgeDocuments.AsNoTracking().Where(x => x.Id == c)
                .Select(x => new { x.Id, x.Version, x.Status, x.UpdatedAtUtc, x.SupersedesDocumentId }).SingleOrDefaultAsync(ct);
            if (older is null) break;
            history.Add(older);
            cursor = older.SupersedesDocumentId;
        }
        var revisions = await db.KnowledgeDocuments.AsNoTracking().Where(x => x.SupersedesDocumentId == id)
            .Select(x => new { x.Id, x.Version, x.Status, x.UpdatedAtUtc }).ToListAsync(ct);
        var people = await EmailsAsync([d.CreatedByUserId, d.ReviewedByUserId], ct);
        return Ok(new
        {
            d.Id, d.Title, d.Category, d.Organization, d.SourceUrl, d.Language, d.PublicationDate, d.Version, d.Status, d.Content,
            d.ContentHash, d.RejectionReason, d.CreatedAtUtc, d.UpdatedAtUtc, d.ReviewedAtUtc,
            author = d.CreatedByUserId is { } a ? people.GetValueOrDefault(a) : "Seed/CLI",
            reviewer = d.ReviewedByUserId is { } r ? people.GetValueOrDefault(r) : null,
            chunks = d.Chunks.OrderBy(c => c.Title).Select(c => new { c.Id, c.Title, c.Content, words = c.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length }),
            previousVersions = history, revisions,
        });
    }

    /// <summary>Validates and chunks content without saving; reports duplicates.</summary>
    [HttpPost("documents/preview")]
    [AdminPermission(Permissions.KnowledgeCreate)]
    public async Task<IActionResult> Preview([FromBody] KnowledgeInput input, CancellationToken ct)
    {
        KnowledgeDocument valid;
        try
        {
            valid = knowledge.Validate(input);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Status = 400, Title = ex.Message });
        }
        var duplicate = await db.KnowledgeDocuments.AsNoTracking().Where(d => d.ContentHash == valid.ContentHash)
            .Select(d => new { d.Id, d.Title, d.Status }).FirstOrDefaultAsync(ct);
        var chunks = knowledge.PreviewChunks(valid.Content);
        return Ok(new { chunks = chunks.Select((c, i) => new { index = i + 1, content = c, words = c.Split(' ').Length }), duplicate });
    }

    [HttpPost("documents")]
    [AdminPermission(Permissions.KnowledgeCreate)]
    public async Task<IActionResult> Create([FromBody] KnowledgeInput input, CancellationToken ct)
    {
        var document = await knowledge.CreateDraftAsync(input, User.UserId(), null, ct);
        return Ok(new { document.Id, document.Status, document.Version });
    }

    /// <summary>Parses an uploaded .txt, .md or .json file into draft fields for review before saving.</summary>
    [HttpPost("documents/import")]
    [AdminPermission(Permissions.KnowledgeCreate)]
    [RequestSizeLimit(KnowledgeAdminService.MaxPdfBytes + 64 * 1024)]
    public async Task<IActionResult> Import(IFormFile file, [FromForm] string? title, [FromForm] string? category,
        [FromForm] string? organization, [FromForm] string? sourceUrl, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new ProblemDetails { Status = 400, Title = "Choose a file." });
        var isPdf = Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        if (file.Length > (isPdf ? KnowledgeAdminService.MaxPdfBytes : KnowledgeAdminService.MaxImportBytes))
            return BadRequest(new ProblemDetails { Status = 400, Title = isPdf ? "PDF files must be at most 5 MB." : "Files must be at most 256 KB." });
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);
        var parsed = KnowledgeAdminService.ParseUpload(file.FileName, stream.ToArray(),
            new KnowledgeInput(title, category, organization, sourceUrl, null, null));
        return Ok(parsed);
    }

    [HttpPut("documents/{id:guid}")]
    [AdminPermission(Permissions.KnowledgeEdit)]
    public async Task<IActionResult> Edit(Guid id, [FromBody] KnowledgeInput input, CancellationToken ct)
    {
        var document = await knowledge.EditAsync(id, input, User.UserId(), ct);
        return Ok(new { document.Id, document.Status, document.Version, newVersion = document.Id != id });
    }

    [HttpPost("documents/{id:guid}/submit")]
    [AdminPermission(Permissions.KnowledgeEdit)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct)
    {
        await knowledge.SubmitAsync(id, ct);
        return NoContent();
    }

    [HttpPost("documents/{id:guid}/approve")]
    [AdminPermission(Permissions.KnowledgeApprove)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct) =>
        Ok(new { indexingJobId = await knowledge.ApproveAsync(id, User.UserId(), ct) });

    [HttpPost("documents/{id:guid}/reject")]
    [AdminPermission(Permissions.KnowledgeReview)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] ReasonRequest request, CancellationToken ct)
    {
        await knowledge.RejectAsync(id, request.Reason, User.UserId(), ct);
        return NoContent();
    }

    [HttpPost("documents/{id:guid}/archive")]
    [AdminPermission(Permissions.KnowledgeArchive)]
    public async Task<IActionResult> Archive(Guid id, [FromBody] ReasonRequest request, CancellationToken ct)
    {
        await knowledge.ArchiveAsync(id, request.Reason, ct);
        return NoContent();
    }

    [HttpPost("documents/{id:guid}/restore")]
    [AdminPermission(Permissions.KnowledgeEdit)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        var draft = await knowledge.RestoreAsDraftAsync(id, User.UserId(), ct);
        return Ok(new { draft.Id, draft.Version });
    }

    /// <summary>Embedding coverage per model, and worker readiness.</summary>
    [HttpGet("coverage")]
    [AdminPermission(Permissions.KnowledgeRead)]
    public async Task<IActionResult> Coverage(CancellationToken ct)
    {
        var approved = await db.KnowledgeChunks.CountAsync(c => c.Document != null && c.Document.Approved && c.Document.Language == "en", ct);
        var models = new List<object>();
        string? storage = null;
        foreach (var model in new[] { EmbeddingModels.MultilingualE5Small, EmbeddingModels.MiniLm })
        {
            int? indexed = null;
            if (db.Database.IsNpgsql())
            {
                try
                {
                    indexed = await KnowledgeVectors.CountIndexedApprovedChunksAsync(db, model);
                }
                catch (Exception ex) when (ex is Npgsql.NpgsqlException or InvalidOperationException)
                {
                    storage = "Vector storage is not enabled (run `knowledge enable-vector` on a pgvector database).";
                }
            }
            else storage = "Vector search needs PostgreSQL with pgvector.";
            models.Add(new
            {
                model, dimensions = EmbeddingModels.Dimensions, active = model == embeddings.Model, indexed,
                missing = indexed is { } i ? approved - i : (int?)null,
                coverage = indexed is { } j && approved > 0 ? Math.Round((double)j / approved, 3) : (double?)null,
            });
        }
        string worker = "NotConfigured";
        if (embeddings.Enabled)
        {
            try
            {
                var client = http.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                using var response = await client.GetAsync(new Uri(new Uri(configuration["Knowledge:EmbeddingUrl"]!), "/ready"), ct);
                worker = response.IsSuccessStatusCode ? "Ready" : "NotReady";
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
            {
                worker = "Unreachable";
            }
        }
        var lastJobs = await db.BackgroundJobs.AsNoTracking()
            .Where(j => j.Type == BackgroundJobs.KnowledgeIndex || j.Type == BackgroundJobs.KnowledgeReindexAll)
            .OrderByDescending(j => j.CreatedAtUtc).Take(10)
            .Select(j => new { j.Id, j.Type, j.Status, j.Progress, j.CreatedAtUtc, j.FinishedAtUtc, j.Error }).ToListAsync(ct);
        return Ok(new { activeModel = embeddings.Model, approvedChunks = approved, worker, storage, models, recentJobs = lastJobs });
    }

    /// <summary>Queues (re)indexing of one approved document or all of them for the active model.</summary>
    [HttpPost("reindex")]
    [AdminPermission(Permissions.KnowledgeReindex)]
    public async Task<IActionResult> Reindex([FromBody] ReindexRequest request, CancellationToken ct)
    {
        if (!embeddings.Enabled) return Conflict(new ProblemDetails { Status = 409, Title = "The embedding worker is not configured (Knowledge:EmbeddingUrl)." });
        var id = request.DocumentId is { } doc
            ? await jobs.EnqueueAsync(BackgroundJobs.KnowledgeIndex, new { documentId = doc }, User.UserId(), ct)
            : await jobs.EnqueueAsync(BackgroundJobs.KnowledgeReindexAll, new { model = embeddings.Model }, User.UserId(), ct);
        await HttpContext.RequestServices.GetRequiredService<AdminAudit>().WriteAsync("knowledge.reindex", resourceType: "BackgroundJob",
            resourceId: id.ToString(), summary: request.DocumentId is null ? $"All approved documents ({embeddings.Model})" : $"Document {request.DocumentId}", cancellationToken: ct);
        return Ok(new { jobId = id });
    }

    private async Task<HashSet<Guid>?> IndexedChunksAsync(List<Guid> chunkIds, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || !embeddings.Enabled || chunkIds.Count == 0) return chunkIds.Count == 0 ? [] : null;
        try
        {
            var model = embeddings.Model;
            var ids = chunkIds.ToArray();
            return (await db.Database.SqlQuery<Guid>(
                $"""SELECT "ChunkId" AS "Value" FROM "KnowledgeEmbeddings" WHERE "Model" = {model} AND "ChunkId" = ANY({ids})""").ToListAsync(ct)).ToHashSet();
        }
        catch (Exception ex) when (ex is Npgsql.NpgsqlException or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<Dictionary<Guid, string>> EmailsAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.OfType<Guid>().Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
    }
}
