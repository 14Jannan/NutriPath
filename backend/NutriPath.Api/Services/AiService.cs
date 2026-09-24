using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.DTOs;
using NutriPath.Api.Models;

namespace NutriPath.Api.Services;

/// <summary>
/// Ties the two kinds of retrieval together:
/// STRUCTURED (AiContextBuilder — exact numbers from EF Core queries, since
/// "this user's calories today" has exactly one right answer) and
/// SEMANTIC (KnowledgeRetrievalService — TF-IDF similarity over curated
/// nutrition knowledge, where relevant text can be phrased many ways).
/// </summary>
public class AiService : IAiService
{
    private readonly IAiContextBuilder _contextBuilder;
    private readonly IKnowledgeRetrievalService _retrieval;
    private readonly IGroqClient _groqClient;
    private readonly NutriPathDbContext _db;

    public AiService(
        IAiContextBuilder contextBuilder,
        IKnowledgeRetrievalService retrieval,
        IGroqClient groqClient,
        NutriPathDbContext db)
    {
        _contextBuilder = contextBuilder;
        _retrieval = retrieval;
        _groqClient = groqClient;
        _db = db;
    }

    public async Task<ChatResponse> ChatAsync(Guid userId, string question)
    {
        // ---- 1. STRUCTURED retrieval: exact facts, plain EF Core queries ----
        var structuredContextJson = await _contextBuilder.BuildContextAsync(userId, question);

        // ---- 2. SEMANTIC retrieval: relevant knowledge, similarity search ----
        await _retrieval.EnsureIndexBuiltAsync();
        var relevantChunks = _retrieval.Search(question, topK: 3);
        var knowledgeText = relevantChunks.Count == 0
            ? "(none found)"
            : string.Join("\n\n", relevantChunks.Select(c => $"[{c.Title}]: {c.Content}"));

        // ---- 3. Build the grounded prompt ----
        // Explicit numbered rules, because models follow specific structured
        // instructions far more reliably than a vague "be accurate". Rule 1
        // is what enforces "the AI never invents nutrition values" — though
        // it's an instruction, not a hard constraint, which is why the
        // backend (not the AI) stays the source of truth for every number.
        var systemPrompt = """
            You are NutriPath's nutrition assistant. You explain nutrition data and give
            general wellness guidance for a Sri Lankan university student. STRICT RULES:
            1) Never invent or alter any numeric value — only use numbers given to you in
               the structured data. If a number isn't there, say so plainly.
            2) If asked something the structured data or reference knowledge doesn't cover,
               say so honestly rather than guessing. Politely decline questions unrelated
               to nutrition or wellness.
            3) Never provide medical diagnosis or treatment advice — this is wellness guidance
               only, and say so if a question strays toward a medical concern.
            4) When suggesting what to eat, only suggest foods listed in "mealSuggestionCandidates"
               or "relevantFoodsFromDatabase", using their listed values (per servingSizeGrams).
               Never invent a dish or its nutrition values. If both lists are empty, say there
               are no suitable options in the food database right now.
            5) Never suggest a food that matches one of the user's "allergies".
            6) Keep answers concise and encouraging, 2-4 sentences unless asked for detail.
            """;

        var userPrompt =
            $"USER'S CURRENT DATA (use these exact numbers, never estimate your own):\n{structuredContextJson}\n\n" +
            $"RELEVANT NUTRITION KNOWLEDGE (use if relevant to the question):\n{knowledgeText}\n\n" +
            $"USER'S QUESTION:\n{question}";

        // ---- 4. Call Groq ----
        var answer = await _groqClient.AskAsync(systemPrompt, userPrompt);

        // ---- 5. Persist the conversation, WITH what was retrieved ----
        var conversation = await _db.AiConversations
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.StartedAtUtc)
            .FirstOrDefaultAsync();

        if (conversation == null)
        {
            conversation = new AiConversation { UserId = userId };
            _db.AiConversations.Add(conversation);
        }

        var sources = relevantChunks.Select(c => c.Title).ToList();

        _db.AiMessages.Add(new AiMessage
        {
            ConversationId = conversation.Id,
            Role = AiMessageRole.User,
            Content = question,
        });
        _db.AiMessages.Add(new AiMessage
        {
            ConversationId = conversation.Id,
            Role = AiMessageRole.Assistant,
            Content = answer,
            // Stored so any answer can later be audited against the exact
            // grounding data and knowledge chunks the model was given.
            RetrievedContextJson = JsonSerializer.Serialize(new
            {
                structuredContext = JsonDocument.Parse(structuredContextJson).RootElement,
                knowledgeChunks = sources,
            }),
        });

        await _db.SaveChangesAsync();

        return new ChatResponse(answer, sources);
    }

    public async Task<List<ChatHistoryMessage>> GetHistoryAsync(Guid userId)
    {
        // Same "latest conversation" rule ChatAsync appends to, so the
        // history shown is exactly the thread new messages continue.
        var conversationId = await _db.AiConversations
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.StartedAtUtc)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync();

        if (conversationId == null) return new List<ChatHistoryMessage>();

        // Both messages of a turn get almost the same timestamp, so Role
        // breaks ties to keep each question ahead of its answer.
        return await _db.AiMessages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAtUtc)
            .ThenBy(m => m.Role)
            .Select(m => new ChatHistoryMessage(m.Id, m.Role.ToString(), m.Content, m.CreatedAtUtc))
            .ToListAsync();
    }
}
