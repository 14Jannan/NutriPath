using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NutriPath.Api.DTOs;
using NutriPath.Api.Services;

namespace NutriPath.Api.Controllers;

/// <summary>Backend-calculated daily progress and the weekly nutrition score.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NutritionController : ControllerBase
{
    private readonly INutritionService _nutritionService;
    private readonly IWeeklyScoreService _weeklyScoreService;

    public NutritionController(INutritionService nutritionService, IWeeklyScoreService weeklyScoreService)
    {
        _nutritionService = nutritionService;
        _weeklyScoreService = weeklyScoreService;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Gets calories eaten vs. target, and macro progress, for one day.</summary>
    /// <remarks>A target of 0 means the user hasn't set up goals yet.</remarks>
    /// <param name="date">The user's local date (yyyy-MM-dd). Defaults to today in UTC.</param>
    [HttpGet("daily")]
    [ProducesResponseType<DailyNutritionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDaily([FromQuery] DateOnly? date)
    {
        if (!ClientDate.TryResolve(date, out var resolved))
            return BadRequest(new { message = ClientDate.InvalidMessage });

        var result = await _nutritionService.GetDailySummaryAsync(CurrentUserId, resolved);
        return Ok(result);
    }

    /// <summary>Gets the deterministic 0-100 weekly score for the 7 days ending on <paramref name="date"/>.</summary>
    /// <remarks>Higher is always better, including for sugar and sodium (100 = within the limit).</remarks>
    /// <param name="date">The user's local date (yyyy-MM-dd). Defaults to today in UTC.</param>
    [HttpGet("weekly-score")]
    [ProducesResponseType<WeeklyScoreResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetWeeklyScore([FromQuery] DateOnly? date)
    {
        if (!ClientDate.TryResolve(date, out var resolved))
            return BadRequest(new { message = ClientDate.InvalidMessage });

        var result = await _weeklyScoreService.GetCurrentWeekScoreAsync(CurrentUserId, resolved);
        return Ok(result);
    }
}
