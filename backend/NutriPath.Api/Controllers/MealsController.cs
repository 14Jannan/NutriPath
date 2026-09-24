using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NutriPath.Api.DTOs;
using NutriPath.Api.Services;

namespace NutriPath.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MealsController : ControllerBase
{
    private readonly IMealService _mealService;

    public MealsController(IMealService mealService)
    {
        _mealService = mealService;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("items")]
    public async Task<IActionResult> LogItem([FromBody] LogMealItemRequest request)
    {
        try
        {
            var result = await _mealService.LogItemAsync(CurrentUserId, request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("daily")]
    public async Task<IActionResult> GetDaily([FromQuery] DateOnly? date)
    {
        var result = await _mealService.GetDailyAsync(CurrentUserId, date ?? DateOnly.FromDateTime(DateTime.UtcNow));
        return Ok(result);
    }

    [HttpDelete("items/{id}")]
    public async Task<IActionResult> DeleteItem(Guid id)
    {
        try
        {
            await _mealService.DeleteItemAsync(CurrentUserId, id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}