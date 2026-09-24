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
    private readonly INutritionCalculationService _nutritionService;
    private readonly IWeeklyScoreService _weeklyScoreService;

    public NutritionController(INutritionCalculationService nutritionService, IWeeklyScoreService weeklyScoreService)
    {
        _nutritionService = nutritionService;
        _weeklyScoreService = weeklyScoreService;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // GET /api/nutrition/daily
    [HttpGet("daily")]
    public async Task<IActionResult> GetDaily()
    {
        var totals = await _nutritionService.GetDailyTotalsAsync(CurrentUserId, DateOnly.FromDateTime(DateTime.UtcNow));
        return Ok(totals);
    }

    // GET /api/nutrition/weekly-score
    [HttpGet("weekly-score")]
    public async Task<IActionResult> GetWeeklyScore()
    {
        var result = await _weeklyScoreService.CalculateAsync(CurrentUserId);
        return Ok(result);
    }
}
