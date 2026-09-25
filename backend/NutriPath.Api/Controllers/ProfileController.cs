using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NutriPath.Api.DTOs;
using NutriPath.Api.Services;

namespace NutriPath.Api.Controllers;

/// <summary>The current user's profile, goals and calculated daily targets.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize] // Every action in this controller requires a valid JWT.
public class ProfileController : ControllerBase
{
    private readonly IProfileService _profileService;
    private readonly IProfileInsightService _insightService;

    public ProfileController(IProfileService profileService, IProfileInsightService insightService)
    {
        _profileService = profileService;
        _insightService = insightService;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // GET /api/profile/me
    /// <summary>Gets the current user's profile and daily targets.</summary>
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
    /// <summary>Saves body stats and goals, and recalculates daily targets (Mifflin-St Jeor).</summary>
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

    /// <summary>Sets the user's avatar, chosen from the fixed set of avatars.</summary>
    /// <response code="400">Not one of the available avatars.</response>
    [HttpPut("avatar")]
    [ProducesResponseType<ProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateAvatar([FromBody] UpdateAvatarRequest request)
    {
        try
        {
            return Ok(await _profileService.UpdateAvatarAsync(CurrentUserId, request.AvatarId));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Calculates targets, BMI and healthy weight range for unsaved values (nothing is stored).</summary>
    /// <remarks>Powers the live preview on the goals screen. Uses exactly the same formulas as saving.</remarks>
    /// <response code="400">A value is out of range or the height/weight pair is implausible.</response>
    [HttpPost("goals/preview")]
    [ProducesResponseType<GoalsPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult PreviewGoals([FromBody] UpdateGoalsRequest request)
    {
        try
        {
            return Ok(_profileService.Preview(request));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Explains, with AI, what the calculated targets mean for this person's age, size and goal.</summary>
    /// <remarks>
    /// Every number is calculated by the backend; the AI only explains them. Limited to 10 requests
    /// per minute per user. If the AI is unavailable, <c>insight</c> is null but the numbers are still returned.
    /// </remarks>
    /// <response code="400">A value is out of range or the height/weight pair is implausible.</response>
    /// <response code="429">Too many insight requests; try again in a minute.</response>
    [HttpPost("insights")]
    [EnableRateLimiting(RateLimitPolicies.AiInsight)]
    [ProducesResponseType<ProfileInsightResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetInsights([FromBody] UpdateGoalsRequest request)
    {
        try
        {
            return Ok(await _insightService.GetInsightAsync(request));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
