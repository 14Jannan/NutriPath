using NutriPath.Api.Data;
using NutriPath.Api.Models;

namespace NutriPath.Api.Services;

public class AiService : IAiService
{
    private readonly IAiContextBuilder _contextBuilder;
    private readonly IGroqClient _groqClient;
    private readonly NutriPathDbContext _db;

    public AiService(IAiContextBuilder contextBuilder, IGroqClient groqClient, NutriPathDbContext db)
    {
        _contextBuilder = contextBuilder;
        _groqClient = groqClient;
        _db = db;
    }

    public async Task<string> ChatAsync(Guid userId, string userMessage)
    {
        var contextJson = await _contextBuilder.BuildContextAsync(userId, userMessage);

        // This system prompt enforces "AI explains, never invents or
        // calculates" from the Phase 0 architecture. It's an instruction,
        // not a hard constraint — which is exactly why the backend, not the
        // AI, remains the source of truth for every number that matters.
        var systemPrompt = $"""
            You are NutriPath's nutrition assistant. You help a Sri Lankan
            university student understand their nutrition data and suggest
            meals. You are NOT a doctor and must never give medical advice —
            only general wellness guidance.

            Here is the user's REAL, CURRENT data, retrieved just now from
            the database. Treat every number in this JSON as ground truth.
            NEVER invent, guess, or recalculate a nutrition number that
            isn't given here — if you don't have a number, say so plainly
            instead of making one up.

            {contextJson}

            Keep responses concise (3-5 sentences unless listing a recipe).
            If suggesting a meal, prefer items from "relevantFoodsFromDatabase"
            when they fit; otherwise suggest a Sri Lankan dish by name without
            inventing specific macro numbers for it.
            """;

        var reply = await _groqClient.AskAsync(systemPrompt, userMessage);

        _db.AiMessages.Add(new AiMessage { UserId = userId, Role = AiMessageRole.User, Content = userMessage });
        _db.AiMessages.Add(new AiMessage { UserId = userId, Role = AiMessageRole.Assistant, Content = reply });
        await _db.SaveChangesAsync();

        return reply;
    }
}
