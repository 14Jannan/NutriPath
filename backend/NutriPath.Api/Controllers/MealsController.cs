using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NutriPath.Api.DTOs;
using NutriPath.Api.Services;

namespace NutriPath.Api.Controllers;

/// <summary>Logging foods into meals and reading a day's log.</summary>
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

    /// <summary>Logs a portion of a food into a meal, creating the meal if needed.</summary>
    /// <remarks>
    /// Nutrients are scaled to the portion and snapshotted at logging time, so later
    /// corrections to the food never change this entry. <c>date</c> is the user's local
    /// date (defaults to today in UTC).
    /// </remarks>
    /// <response code="200">The logged item with its scaled nutrients.</response>
    /// <response code="400">Unknown food, invalid meal type, non-positive quantity or out-of-range date.</response>
    [HttpPost("items")]
    [ProducesResponseType<MealItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LogItem([FromBody] LogMealItemRequest request)
    {
        if (!ClientDate.TryResolve(request.Date, out var date))
            return BadRequest(new { message = ClientDate.InvalidMessage });

        try
        {
            var result = await _mealService.LogItemAsync(CurrentUserId, request with { Date = date });
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Gets one day's meals, grouped by meal type, with day totals.</summary>
    /// <param name="date">The user's local date (yyyy-MM-dd). Defaults to today in UTC.</param>
    [HttpGet("daily")]
    [ProducesResponseType<DailyMealsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDaily([FromQuery] DateOnly? date)
    {
        if (!ClientDate.TryResolve(date, out var resolved))
            return BadRequest(new { message = ClientDate.InvalidMessage });

        var result = await _mealService.GetDailyAsync(CurrentUserId, resolved);
        return Ok(result);
    }

    /// <summary>Deletes one logged item belonging to the current user.</summary>
    /// <response code="204">Deleted.</response>
    /// <response code="404">No such item for this user.</response>
    [HttpDelete("items/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
