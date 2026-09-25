using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NutriPath.Api.DTOs;
using NutriPath.Api.Services;

namespace NutriPath.Api.Controllers;

/// <summary>The grounded AI nutrition assistant, and the user's chat history.</summary>
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
    /// foods); the AI only explains them. Send <c>localDateTime</c> so the assistant knows the
    /// user's date, day and time, and <c>conversationId</c> to continue a specific conversation
    /// (the recent messages are used as memory). Both the question and answer are saved.
    /// </remarks>
    /// <response code="200">The answer, the knowledge sources used, and the conversation id.</response>
    /// <response code="400">Empty question, or an implausible date/time.</response>
    /// <response code="404">The conversation doesn't exist or isn't the user's.</response>
    [HttpPost("chat")]
    [ProducesResponseType<ChatResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { message = "A question is required." });
        if (!ClientDate.TryResolveClock(request.LocalDate, request.LocalDateTime, out var clock))
            return BadRequest(new { message = "Your device's date or time looks wrong. Please check your clock." });

        try
        {
            var response = await _aiService.ChatAsync(CurrentUserId, request.Question, clock, request.ConversationId);
            return Ok(response);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>Gets the most recent conversation, oldest message first.</summary>
    [HttpGet("history")]
    [ProducesResponseType<List<ChatHistoryMessage>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory() => Ok(await _aiService.GetHistoryAsync(CurrentUserId));

    /// <summary>Lists the user's past conversations, most recently active first.</summary>
    [HttpGet("conversations")]
    [ProducesResponseType<List<ConversationSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListConversations() => Ok(await _aiService.ListConversationsAsync(CurrentUserId));

    /// <summary>Gets every message of one conversation, oldest first.</summary>
    /// <response code="404">The conversation doesn't exist or isn't the user's.</response>
    [HttpGet("conversations/{id:guid}/messages")]
    [ProducesResponseType<List<ChatHistoryMessage>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConversationMessages(Guid id)
    {
        try
        {
            return Ok(await _aiService.GetConversationMessagesAsync(CurrentUserId, id));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>Searches all of the user's messages (case-insensitive, newest first, up to 30).</summary>
    /// <param name="q">At least 2 characters.</param>
    [HttpGet("search")]
    [ProducesResponseType<List<ChatSearchResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Search([FromQuery] string q) =>
        Ok(await _aiService.SearchAsync(CurrentUserId, q ?? string.Empty));

    /// <summary>Starts a new, empty conversation. Earlier ones stay in the history.</summary>
    [HttpPost("conversations")]
    [ProducesResponseType<NewConversationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> StartNewConversation() =>
        Ok(new NewConversationResponse(await _aiService.StartNewConversationAsync(CurrentUserId)));

    /// <summary>Permanently deletes one conversation and its messages.</summary>
    /// <response code="404">The conversation doesn't exist or isn't the user's.</response>
    [HttpDelete("conversations/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteConversation(Guid id)
    {
        try
        {
            await _aiService.DeleteConversationAsync(CurrentUserId, id);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
