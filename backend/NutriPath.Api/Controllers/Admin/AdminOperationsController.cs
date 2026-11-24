using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Authorization;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Models.Admin;
using NutriPath.Api.Services;
using NutriPath.Api.Services.Admin;

namespace NutriPath.Api.Controllers.Admin;

public sealed record FoodRequest(string Name, string? NameTamil, string? NameSinhala, string? Category, decimal ServingSizeGrams,
    decimal Calories, decimal ProteinGrams, decimal CarbsGrams, decimal FatGrams, decimal FiberGrams, decimal SugarGrams,
    decimal SodiumMilligrams, string? Allergens, bool IsVerified);
public sealed record UsdaSyncRequest(string Query, int PageSize = 10);

/// <summary>Background jobs, system health, the food catalogue and USDA synchronization.</summary>
[Route("api/admin")]
public sealed class AdminOperationsController(NutriPathDbContext db, BackgroundJobs jobs, SystemHealth health,
    AdminAudit audit, IProviderCredentialStore credentials, IHttpClientFactory http) : AdminControllerBase
{
    // ---- Jobs ----

    [HttpGet("jobs")]
    [AdminPermission(Permissions.SystemMonitor)]
    public async Task<IActionResult> Jobs(string? type, string? status, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        (page, pageSize) = Paging(page, pageSize);
        var query = db.BackgroundJobs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(j => j.Type == type);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(j => j.Status == status);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(j => j.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(j => new { j.Id, j.Type, j.Status, j.Progress, j.Attempts, j.Error, j.ResultJson, j.CreatedAtUtc, j.StartedAtUtc, j.FinishedAtUtc, j.CancelRequested, j.CreatedByUserId })
            .ToListAsync(ct);
        var ids = items.Select(i => i.CreatedByUserId).OfType<Guid>().Distinct().ToList();
        var people = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
        return Ok(new PagedResult<object>(items.Select(i => (object)new
        {
            i.Id, i.Type, i.Status, i.Progress, i.Attempts, i.Error, result = i.ResultJson, i.CreatedAtUtc, i.StartedAtUtc, i.FinishedAtUtc,
            i.CancelRequested, createdBy = i.CreatedByUserId is { } c ? people.GetValueOrDefault(c) : null,
        }).ToList(), total, page, pageSize));
    }

    [HttpPost("jobs/{id:guid}/cancel")]
    [AdminPermission(Permissions.SystemJobsManage)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var job = await db.BackgroundJobs.SingleOrDefaultAsync(j => j.Id == id, ct);
        if (job is null) return NotFound();
        if (job.Status is "Succeeded" or "Failed" or "Cancelled") return Conflict(new ProblemDetails { Status = 409, Title = "The job has already finished." });
        job.CancelRequested = true;
        if (job.Status == "Queued")
        {
            job.Status = "Cancelled";
            job.FinishedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("job.cancel", resourceType: "BackgroundJob", resourceId: id.ToString(), cancellationToken: ct);
        return NoContent();
    }

    [HttpPost("jobs/{id:guid}/retry")]
    [AdminPermission(Permissions.SystemJobsManage)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct)
    {
        var job = await db.BackgroundJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id, ct);
        if (job is null) return NotFound();
        if (job.Status is not ("Failed" or "Cancelled")) return Conflict(new ProblemDetails { Status = 409, Title = "Only failed or cancelled jobs can be retried." });
        var newId = await jobs.EnqueueAsync(job.Type, JsonSerializer.Deserialize<JsonElement>(job.PayloadJson), User.UserId(), ct);
        await audit.WriteAsync("job.retry", resourceType: "BackgroundJob", resourceId: newId.ToString(), summary: $"Retry of {id}", cancellationToken: ct);
        return Ok(new { jobId = newId });
    }

    // ---- Health ----

    [HttpGet("health")]
    [AdminPermission(Permissions.SystemMonitor)]
    public async Task<IActionResult> Health(bool refresh = false, CancellationToken ct = default) =>
        Ok(new { components = await health.SummaryAsync(ct, refresh), note = "External APIs are not called by this check; use Test connection on the AI Providers and API Keys pages." });

    // ---- Foods ----

    [HttpGet("foods")]
    [AdminPermission(Permissions.FoodsRead)]
    public async Task<IActionResult> Foods(string? search, string? source, bool archived = false, string? sort = null,
        int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        (page, pageSize) = Paging(page, pageSize);
        var query = db.Foods.AsNoTracking().Where(f => f.CreatedByUserId == null && f.IsArchived == archived);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = SearchPatterns.Contains(search.Trim());
            query = query.Where(f => EF.Functions.ILike(f.Name, pattern, SearchPatterns.EscapeCharacter) ||
                                     (f.NameTamil != null && EF.Functions.ILike(f.NameTamil, pattern, SearchPatterns.EscapeCharacter)) ||
                                     (f.NameSinhala != null && EF.Functions.ILike(f.NameSinhala, pattern, SearchPatterns.EscapeCharacter)));
        }
        if (!string.IsNullOrWhiteSpace(source)) query = query.Where(f => f.DataSource!.Name == source);
        var total = await query.CountAsync(ct);
        query = sort switch
        {
            "calories" => query.OrderByDescending(f => f.Calories),
            "protein" => query.OrderByDescending(f => f.ProteinGrams),
            "updated" => query.OrderByDescending(f => f.LastUpdatedAtUtc),
            _ => query.OrderBy(f => f.Name),
        };
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).Select(f => new
        {
            f.Id, f.Name, f.NameTamil, f.NameSinhala, f.Category, source = f.DataSource!.Name, f.ServingSizeGrams, f.Calories,
            f.ProteinGrams, f.CarbsGrams, f.FatGrams, f.FiberGrams, f.SugarGrams, f.SodiumMilligrams, f.Allergens,
            allergenStatus = f.Allergens == null ? "Unknown" : f.Allergens == "" ? "NoneDeclared" : "Declared",
            f.IsVerified, f.IsArchived, f.LastUpdatedAtUtc,
        }).ToListAsync(ct);
        var sources = await db.DataSources.AsNoTracking().Select(s => s.Name).OrderBy(n => n).ToListAsync(ct);
        return Ok(new { total, page, pageSize, sources, items });
    }

    [HttpPost("foods")]
    [AdminPermission(Permissions.FoodsManage)]
    public async Task<IActionResult> CreateFood([FromBody] FoodRequest request, CancellationToken ct)
    {
        Validate(request);
        var name = request.Name.Trim();
        if (await db.Foods.AnyAsync(f => f.CreatedByUserId == null && !f.IsArchived && f.Name.ToLower() == name.ToLower(), ct))
            return Conflict(new ProblemDetails { Status = 409, Title = "A catalogue food with this name already exists." });
        var source = await CuratedSourceAsync(ct);
        var food = new Food { DataSourceId = source.Id, ExternalId = "admin:" + Guid.NewGuid().ToString("N") };
        Apply(food, request);
        db.Foods.Add(food);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("food.create", resourceType: "Food", resourceId: food.Id.ToString(), summary: food.Name, cancellationToken: ct);
        return Ok(new { food.Id });
    }

    /// <summary>Updates a catalogue food. Logged meals keep their snapshot values.</summary>
    [HttpPut("foods/{id:guid}")]
    [AdminPermission(Permissions.FoodsManage)]
    public async Task<IActionResult> UpdateFood(Guid id, [FromBody] FoodRequest request, CancellationToken ct)
    {
        Validate(request);
        var food = await db.Foods.SingleOrDefaultAsync(f => f.Id == id && f.CreatedByUserId == null, ct);
        if (food is null) return NotFound();
        var before = $"{food.Calories} kcal, {food.ProteinGrams} g protein per {food.ServingSizeGrams} g";
        Apply(food, request);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("food.update", resourceType: "Food", resourceId: id.ToString(),
            summary: $"{food.Name}: {before} → {food.Calories} kcal, {food.ProteinGrams} g protein per {food.ServingSizeGrams} g", cancellationToken: ct);
        return NoContent();
    }

    [HttpPost("foods/{id:guid}/archive")]
    [AdminPermission(Permissions.FoodsManage)]
    public Task<IActionResult> ArchiveFood(Guid id, CancellationToken ct) => SetArchived(id, true, ct);

    [HttpPost("foods/{id:guid}/restore")]
    [AdminPermission(Permissions.FoodsManage)]
    public Task<IActionResult> RestoreFood(Guid id, CancellationToken ct) => SetArchived(id, false, ct);

    // ---- USDA ----

    /// <summary>Searches USDA FoodData Central for a preview (no import). Uses the active USDA key.</summary>
    [HttpGet("foods/usda/search")]
    [AdminPermission(Permissions.FoodsSync)]
    public async Task<IActionResult> UsdaSearch(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 100) return BadRequest(new ProblemDetails { Status = 400, Title = "Enter a search term." });
        var key = await credentials.GetActiveCredentialAsync(ExternalProviders.Usda, ct);
        if (key is null) return Conflict(new ProblemDetails { Status = 409, Title = "No USDA API key is configured." });
        var client = http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        try
        {
            var result = await client.GetFromJsonAsync<UsdaFoodSearchResponse>(
                $"https://api.nal.usda.gov/fdc/v1/foods/search?query={Uri.EscapeDataString(query)}&pageSize=10&api_key={Uri.EscapeDataString(key)}", ct);
            var ids = (result?.Foods ?? []).Select(f => f.FdcId.ToString()).ToList();
            var existing = await db.Foods.AsNoTracking().Where(f => ids.Contains(f.ExternalId)).Select(f => f.ExternalId).ToListAsync(ct);
            return Ok((result?.Foods ?? []).Select(f => new
            {
                f.FdcId, f.Description, imported = existing.Contains(f.FdcId.ToString()),
                // USDA nutrient ids: 1008 energy (kcal), 1003 protein, 1005 carbohydrate, 1004 fat, 1079 fibre.
                nutrients = f.FoodNutrients.Where(n => n.NutrientId is 1008 or 1003 or 1004 or 1005 or 1079)
                    .Select(n => new
                    {
                        name = n.NutrientId switch { 1008 => "Energy (kcal)", 1003 => "Protein (g)", 1005 => "Carbohydrate (g)", 1004 => "Fat (g)", _ => "Fibre (g)" },
                        n.Value,
                    }),
            }));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return StatusCode(502, new ProblemDetails { Status = 502, Title = "USDA FoodData Central could not be reached." });
        }
    }

    [HttpPost("foods/usda/sync")]
    [AdminPermission(Permissions.FoodsSync)]
    public async Task<IActionResult> UsdaSync([FromBody] UsdaSyncRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Query) || request.Query.Length > 100) return BadRequest(new ProblemDetails { Status = 400, Title = "Enter a search term." });
        var id = await jobs.EnqueueAsync(BackgroundJobs.UsdaSync, new { query = request.Query.Trim(), pageSize = Math.Clamp(request.PageSize, 1, 50) }, User.UserId(), ct);
        await audit.WriteAsync("usda.sync", resourceType: "BackgroundJob", resourceId: id.ToString(), summary: $"\"{request.Query.Trim()}\"", cancellationToken: ct);
        return Ok(new { jobId = id });
    }

    [HttpGet("foods/usda/history")]
    [AdminPermission(Permissions.FoodsRead)]
    public async Task<IActionResult> UsdaHistory(CancellationToken ct) =>
        Ok(await db.SyncJobs.AsNoTracking().OrderByDescending(s => s.StartedAtUtc).Take(50)
            .Select(s => new { s.Id, s.StartedAtUtc, s.CompletedAtUtc, status = s.Status.ToString(), s.RecordsFetched, s.RecordsInserted, s.RecordsUpdated, s.RecordsFailed })
            .ToListAsync(ct));

    private async Task<IActionResult> SetArchived(Guid id, bool archived, CancellationToken ct)
    {
        var food = await db.Foods.SingleOrDefaultAsync(f => f.Id == id && f.CreatedByUserId == null, ct);
        if (food is null) return NotFound();
        food.IsArchived = archived;
        food.LastUpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(archived ? "food.archive" : "food.restore", resourceType: "Food", resourceId: id.ToString(), summary: food.Name, cancellationToken: ct);
        return NoContent();
    }

    private static void Validate(FoodRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 200) throw new AdminRuleException("Enter a name (up to 200 characters).", 400);
        if (r.ServingSizeGrams is <= 0 or > 5000) throw new AdminRuleException("Serving size must be between 0 and 5,000 g.", 400);
        var values = new[] { r.Calories, r.ProteinGrams, r.CarbsGrams, r.FatGrams, r.FiberGrams, r.SugarGrams, r.SodiumMilligrams };
        if (values.Any(v => v < 0)) throw new AdminRuleException("Nutrient values can't be negative.", 400);
        if (r.Calories > 9 * r.ServingSizeGrams + 1) throw new AdminRuleException("Calories are higher than physically possible for this serving (over 9 kcal per gram).", 400);
        if (r.ProteinGrams + r.CarbsGrams + r.FatGrams > r.ServingSizeGrams * 1.05m)
            throw new AdminRuleException("Protein, carbohydrate and fat add up to more than the serving weight.", 400);
        if (r.Allergens is { Length: > 300 }) throw new AdminRuleException("Allergen list is too long.", 400);
    }

    private static void Apply(Food food, FoodRequest r)
    {
        food.Name = r.Name.Trim();
        food.NameTamil = string.IsNullOrWhiteSpace(r.NameTamil) ? null : r.NameTamil.Trim().Normalize(System.Text.NormalizationForm.FormC);
        food.NameSinhala = string.IsNullOrWhiteSpace(r.NameSinhala) ? null : r.NameSinhala.Trim().Normalize(System.Text.NormalizationForm.FormC);
        food.Category = string.IsNullOrWhiteSpace(r.Category) ? null : r.Category.Trim();
        food.ServingSizeGrams = r.ServingSizeGrams;
        food.Calories = r.Calories;
        food.ProteinGrams = r.ProteinGrams;
        food.CarbsGrams = r.CarbsGrams;
        food.FatGrams = r.FatGrams;
        food.FiberGrams = r.FiberGrams;
        food.SugarGrams = r.SugarGrams;
        food.SodiumMilligrams = r.SodiumMilligrams;
        // null = not known; "" = none declared (still not a guarantee of allergy safety).
        food.Allergens = r.Allergens?.Trim();
        food.IsVerified = r.IsVerified;
        food.LastUpdatedAtUtc = DateTime.UtcNow;
    }

    private async Task<DataSource> CuratedSourceAsync(CancellationToken ct)
    {
        var source = await db.DataSources.SingleOrDefaultAsync(s => s.Name == "NutriPath curated", ct);
        if (source is not null) return source;
        source = new DataSource { Name = "NutriPath curated", Authority = "NutriPath nutrition reviewers", License = "Internal", Type = DataSourceType.ManualSeed };
        db.DataSources.Add(source);
        await db.SaveChangesAsync(ct);
        return source;
    }
}
