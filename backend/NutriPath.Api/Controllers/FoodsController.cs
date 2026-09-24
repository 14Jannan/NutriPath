using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NutriPath.Api.DTOs;
using NutriPath.Api.Services;

namespace NutriPath.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FoodsController : ControllerBase
{
    private readonly IFoodSearchService _foodSearch;

    public FoodsController(IFoodSearchService foodSearch)
    {
        _foodSearch = foodSearch;
    }

    // GET /api/foods/search?query=rice&page=1&pageSize=20
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string query, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            return Ok(new List<FoodSearchResultDto>()); // don't search on empty/1-char input
        }

        // Clamped so page=0 can't produce a negative Skip (which throws)
        // and a huge pageSize can't pull the whole foods table.
        var results = await _foodSearch.SearchAsync(query.Trim(), Math.Max(page, 1), Math.Clamp(pageSize, 1, 50));
        return Ok(results);
    }

    // GET /api/foods/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var food = await _foodSearch.GetByIdAsync(id);
        return food == null ? NotFound() : Ok(food);
    }
}
