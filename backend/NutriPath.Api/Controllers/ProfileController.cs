using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NutriPath.Api.DTOs;
using NutriPath.Api.Services;

namespace NutriPath.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Every action in this controller requires a valid JWT.
public class ProfileController : ControllerBase
{
    private readonly IProfileService _profileService;

    public ProfileController(IProfileService profileService)
    {
        _profileService = profileService;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // GET /api/profile/me
    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile()
    {
        try
        {
            return Ok(await _profileService.GetMyProfileAsync(CurrentUserId));
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    // PUT /api/profile/goals
    [HttpPut("goals")]
    public async Task<IActionResult> UpdateGoals([FromBody] UpdateGoalsRequest request)
    {
        try
        {
            return Ok(await _profileService.UpdateGoalsAsync(CurrentUserId, request));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
