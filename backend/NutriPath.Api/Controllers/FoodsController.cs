using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NutriPath.Api.DTOs;
using NutriPath.Api.Services;

namespace NutriPath.Api.Controllers;

/// <summary>Searching the food database.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FoodsController : ControllerBase
{
    private readonly IFoodSearchService _foodSearch;
    private readonly IFoodLookupService _foodLookup;

    public FoodsController(IFoodSearchService foodSearch, IFoodLookupService foodLookup)
    {
        _foodSearch = foodSearch;
        _foodLookup = foodLookup;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // GET /api/foods/search?query=rice&page=1&pageSize=20
    /// <summary>Case-insensitive food search by name (at least 2 characters), paged.</summary>
    /// <remarks>
    /// Includes the user's own foods. If the catalog has few matches, the term is looked up
    /// in USDA once (then cached for 12 hours) and the new foods are included.
    /// </remarks>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string query, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            return Ok(new List<FoodSearchResultDto>()); // don't search on empty/1-char input
        }

        // Clamped so page=0 can't produce a negative Skip (which throws)
        // and a huge pageSize can't pull the whole foods table.
        var results = await _foodLookup.SearchAsync(CurrentUserId, query.Trim(), Math.Max(page, 1), Math.Clamp(pageSize, 1, 50));
        return Ok(results);
    }

    // GET /api/foods/{id}
    /// <summary>Gets one food with its nutrients per serving.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var food = await _foodSearch.GetByIdAsync(CurrentUserId, id);
        return food == null ? NotFound() : Ok(food);
    }

    /// <summary>Adds a private food with nutrients per 100 g, for foods the catalog doesn't have.</summary>
    /// <remarks>
    /// Only the user who adds it can see, log or be suggested it: its values aren't verified.
    /// Values are checked for plausibility (e.g. calories must roughly match protein, carbs and fat).
    /// </remarks>
    /// <response code="400">A value is implausible, or the user already has a food with that name.</response>
    [HttpPost]
    [ProducesResponseType<FoodSearchResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateFoodRequest request)
    {
        try
        {
            return Ok(await _foodSearch.CreateCustomAsync(CurrentUserId, request));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Deletes one of the user's own foods, if it isn't used in their meal log.</summary>
    /// <response code="404">No such food of the user's own.</response>
    /// <response code="409">The food is in the meal log.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            await _foodSearch.DeleteCustomAsync(CurrentUserId, id);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}
