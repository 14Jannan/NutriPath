using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NutriPath.Api.DTOs;
using NutriPath.Api.Services;

namespace NutriPath.Api.Controllers;

/// <summary>The grounded AI nutrition assistant.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AiController : ControllerBase
{
    private readonly IAiService _aiService;

    public AiController(IAiService aiService) => _aiService = aiService;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Asks the assistant a question, grounded in the user's real data.</summary>
    /// <remarks>
    /// The backend supplies every number (targets, today's totals, weekly score, database
    /// foods); the AI only explains them. Both the question and answer are saved.
    /// </remarks>
    /// <response code="200">The answer and the knowledge sources used.</response>
    /// <response code="400">Empty question or out-of-range date.</response>
    [HttpPost("chat")]
    [ProducesResponseType<ChatResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { message = "A question is required." });
        if (!ClientDate.TryResolve(request.LocalDate, out var today))
            return BadRequest(new { message = ClientDate.InvalidMessage });

        var response = await _aiService.ChatAsync(CurrentUserId, request.Question, today);
        return Ok(response);
    }

    /// <summary>Gets the current conversation, oldest message first.</summary>
    [HttpGet("history")]
    [ProducesResponseType<List<ChatHistoryMessage>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory() => Ok(await _aiService.GetHistoryAsync(CurrentUserId));

    /// <summary>Starts a new, empty conversation. Earlier ones are kept but no longer shown.</summary>
    [HttpPost("conversations")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> StartNewConversation()
    {
        await _aiService.StartNewConversationAsync(CurrentUserId);
        return NoContent();
    }
}
