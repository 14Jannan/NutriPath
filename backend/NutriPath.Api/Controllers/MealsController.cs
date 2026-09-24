using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.DTOs;
using NutriPath.Api.Models;

namespace NutriPath.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MealsController : ControllerBase
{
    private readonly NutriPathDbContext _db;

    public MealsController(NutriPathDbContext db) => _db = db;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // POST /api/meals — logs one food into today's meal of the given type,
    // creating that meal if it doesn't exist yet.
    [HttpPost]
    public async Task<IActionResult> LogItem([FromBody] LogMealItemRequest request)
    {
        if (request.Servings <= 0) return BadRequest(new { message = "Servings must be greater than zero." });

        // Checked up front so an unknown id returns a clear 404 instead of
        // a foreign-key violation surfacing as a 500 on SaveChanges.
        if (!await _db.Foods.AnyAsync(f => f.Id == request.FoodId))
            return NotFound(new { message = "Food not found." });

        var userId = CurrentUserId;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var meal = await _db.Meals
            .Include(m => m.Items)
            .FirstOrDefaultAsync(m => m.UserId == userId && m.Type == request.MealType && m.Date == today);

        if (meal == null)
        {
            meal = new Meal { UserId = userId, Type = request.MealType, Date = today };
            _db.Meals.Add(meal);
        }

        meal.Items.Add(new MealItem { MealId = meal.Id, FoodId = request.FoodId, Servings = request.Servings });
        await _db.SaveChangesAsync();

        return Ok(new { message = "Logged." });
    }

    // GET /api/meals/today
    [HttpGet("today")]
    public async Task<IActionResult> GetToday()
    {
        var userId = CurrentUserId;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var meals = await _db.Meals
            .Include(m => m.Items).ThenInclude(i => i.Food)
            .Where(m => m.UserId == userId && m.Date == today)
            .ToListAsync();

        // The backend calculates every nutrition number from real data —
        // the AI only ever explains these, never invents them.
        var result = meals.Select(m => new MealResponse(
            m.Type,
            m.Items.Select(i => new MealItemResponse(
                i.Food!.Name,
                i.Servings,
                i.Food.Calories * i.Servings,
                i.Food.ProteinGrams * i.Servings)).ToList(),
            m.Items.Sum(i => i.Food!.Calories * i.Servings)));

        return Ok(result);
    }
}
