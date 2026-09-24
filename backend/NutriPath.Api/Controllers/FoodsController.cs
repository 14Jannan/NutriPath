using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;

namespace NutriPath.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FoodsController : ControllerBase
{
    private readonly NutriPathDbContext _db;

    public FoodsController(NutriPathDbContext db) => _db = db;

    // GET /api/foods/search?query=rice&take=20
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string query, [FromQuery] int take = 20)
    {
        if (string.IsNullOrWhiteSpace(query)) return Ok(Array.Empty<object>());

        // ILIKE = case-insensitive LIKE, Postgres-specific. EF.Functions.ILike
        // is a provider-specific escape hatch — EF Core is database-agnostic
        // by default, but here we deliberately want a Postgres feature.
        var results = await _db.Foods
            .Where(f => EF.Functions.ILike(f.Name, $"%{query}%"))
            .OrderBy(f => f.Name)
            .Take(Math.Clamp(take, 1, 50))
            .ToListAsync();

        return Ok(results);
    }
}
