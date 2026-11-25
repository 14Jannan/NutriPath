using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.Services.Admin;

namespace NutriPath.Api.Controllers;

public sealed record DataRequestInput(string Type, string? Note);

/// <summary>The user's own privacy requests: a copy of their data, or deleting their account.</summary>
[ApiController]
[Route("api/account")]
[Authorize]
public sealed class AccountController(DataRequestService requests, NutriPathDbContext db) : ControllerBase
{
    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>The user's data requests, newest first.</summary>
    [HttpGet("data-requests")]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await db.DataRequests.AsNoTracking().Where(r => r.UserId == CurrentUserId).OrderByDescending(r => r.CreatedAtUtc).Take(20)
            .Select(r => new
            {
                r.Id, r.Type, r.Status, r.CreatedAtUtc, r.ProcessedAtUtc, adminNote = r.AdminNote,
                downloadAvailable = r.Type == "Export" && r.Status == "Completed" && r.ExportCiphertext != null && r.ExportExpiresAtUtc > DateTime.UtcNow,
                r.ExportExpiresAtUtc,
            }).ToListAsync(ct));

    /// <summary>Requests an export (Type = Export) or account deletion (Type = Deletion).</summary>
    [HttpPost("data-requests")]
    public async Task<IActionResult> Create([FromBody] DataRequestInput input, CancellationToken ct)
    {
        try
        {
            var request = await requests.CreateAsync(CurrentUserId, input.Type, input.Note, ct);
            return Ok(new { request.Id, request.Type, request.Status, request.CreatedAtUtc });
        }
        catch (AdminRuleException ex)
        {
            return StatusCode(ex.Status, new { message = ex.Message });
        }
    }

    [HttpPost("data-requests/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        try
        {
            await requests.CancelAsync(CurrentUserId, id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (AdminRuleException ex)
        {
            return StatusCode(ex.Status, new { message = ex.Message });
        }
    }

    /// <summary>Downloads a completed export as JSON (until it expires).</summary>
    [HttpGet("data-requests/{id:guid}/export")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        try
        {
            var json = await requests.DownloadAsync(CurrentUserId, id, ct);
            Response.Headers.CacheControl = "no-store";
            return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", $"nutripath-data-{DateTime.UtcNow:yyyyMMdd}.json");
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (AdminRuleException ex)
        {
            return StatusCode(ex.Status, new { message = ex.Message });
        }
    }
}

/// <summary>Public app status: which optional features are on, and any maintenance notice.</summary>
[ApiController]
[Route("api/app-status")]
[AllowAnonymous]
public sealed class AppStatusController(PlatformSettingsStore settings) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var flags = settings.Flags;
        var system = settings.System;
        Response.Headers.CacheControl = "public, max-age=30";
        return Ok(new
        {
            features = FeatureFlags.All.Where(f => f.ClientVisible).ToDictionary(f => f.Key, f => flags.IsEnabled(f.Key)),
            maintenanceMessage = string.IsNullOrWhiteSpace(system.MaintenanceMessage) ? null : system.MaintenanceMessage,
            supportEmail = system.SupportEmail,
        });
    }
}
