using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;

namespace NutriPath.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Every action in this controller requires a valid JWT.
public class ProfileController : ControllerBase
{
    private readonly NutriPathDbContext _db;

    public ProfileController(NutriPathDbContext db)
    {
        _db = db;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile()
    {
        // The Sub claim we put in the token back in Step 7 is how we know
        // WHO is calling, without them sending their user ID separately —
        // it's already baked into (and verified by) the token itself.
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var user = await _db.Users.Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null) return NotFound();

        return Ok(new
        {
            user.Id,
            user.Email,
            user.EmailVerified,
            FullName = user.Profile?.FullName,
        });
    }
}