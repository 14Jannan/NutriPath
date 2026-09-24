using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NutriPath.Api.Services;

namespace NutriPath.Api.Controllers;

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

    // GET /api/nutrition/daily?date=2026-09-24
    [HttpGet("daily")]
    public async Task<IActionResult> GetDaily([FromQuery] DateOnly? date)
    {
        var result = await _nutritionService.GetDailySummaryAsync(CurrentUserId, date ?? DateOnly.FromDateTime(DateTime.UtcNow));
        return Ok(result);
    }

    // GET /api/nutrition/weekly-score
    [HttpGet("weekly-score")]
    public async Task<IActionResult> GetWeeklyScore()
    {
        var result = await _weeklyScoreService.GetCurrentWeekScoreAsync(CurrentUserId);
        return Ok(result);
    }
}
