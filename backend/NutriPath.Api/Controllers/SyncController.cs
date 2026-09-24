using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.DTOs;
using NutriPath.Api.Services;

namespace NutriPath.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
// Without this, anyone could trigger USDA syncs and spend the API key's quota.
[Authorize]
public class SyncController : ControllerBase
{
    private readonly IUsdaFoodSyncService _usdaSync;
    private readonly NutriPathDbContext _db;

    public SyncController(IUsdaFoodSyncService usdaSync, NutriPathDbContext db)
    {
        _usdaSync = usdaSync;
        _db = db;
    }

    // POST /api/sync/usda?query=rice&pageSize=25
    [HttpPost("usda")]
    public async Task<IActionResult> SyncUsda([FromQuery] string query, [FromQuery] int pageSize = 25)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { message = "A 'query' parameter is required, e.g. ?query=rice" });
        }

        var job = await _usdaSync.SyncAsync(query, pageSize);
        return Ok(job);
    }

    // GET /api/sync/jobs
    [HttpGet("jobs")]
    public async Task<IActionResult> GetJobs()
    {
        var jobs = await _db.SyncJobs
            .OrderByDescending(j => j.StartedAtUtc)
            .Take(20)
            .ToListAsync();
        return Ok(jobs);
    }

    // GET /api/sync/status — per source: how stale it is and how many
    // foods it has contributed, so data freshness can be checked at a glance.
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var sources = await _db.DataSources
            .Select(s => new
            {
                s.Name,
                s.LastSyncedAtUtc,
                FoodCount = _db.Foods.Count(f => f.DataSourceId == s.Id),
            })
            .ToListAsync();

        var now = DateTime.UtcNow;
        var result = sources.Select(s => new DataSourceStatus(
            s.Name,
            s.LastSyncedAtUtc,
            s.LastSyncedAtUtc.HasValue ? (int)(now - s.LastSyncedAtUtc.Value).TotalDays : null,
            s.FoodCount)).ToList();

        return Ok(result);
    }
}