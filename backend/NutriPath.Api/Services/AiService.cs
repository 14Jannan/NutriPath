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

    // How many earlier messages are sent to the AI as memory: enough for
    // natural follow-ups ("what about lunch?"), small enough to stay fast.
    private const int HistoryMessages = 10;
    private const int HistoryMessageMaxChars = 1500;

    public async Task<ChatResponse> ChatAsync(Guid userId, string question, ClientClock clock, Guid? conversationId = null)
    {
        // ---- 0. Which conversation this continues ----
        var conversation = conversationId is { } id
            ? await _db.AiConversations.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId)
                ?? throw new KeyNotFoundException("Conversation not found.")
            : await _db.AiConversations
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.StartedAtUtc)
                .FirstOrDefaultAsync();

        // Earlier turns, oldest first. Only the raw question/answer text is
        // sent — never old context blocks — so stale numbers from earlier
        // turns can't override today's data below.
        var history = conversation == null
            ? new List<GroqMessage>()
            : (await _db.AiMessages
                    .Where(m => m.ConversationId == conversation.Id)
                    .OrderByDescending(m => m.CreatedAtUtc)
                    .ThenByDescending(m => m.Role)
                    .Take(HistoryMessages)
                    .Select(m => new { m.Role, m.Content })
                    .ToListAsync())
                .AsEnumerable()
                .Reverse()
                .Select(m => new GroqMessage
                {
                    Role = m.Role == AiMessageRole.User ? "user" : "assistant",
                    Content = m.Content.Length > HistoryMessageMaxChars ? m.Content[..HistoryMessageMaxChars] : m.Content,
                })
                .ToList();

        // ---- 1. STRUCTURED retrieval: exact facts, plain EF Core queries ----
        var structuredContextJson = await _contextBuilder.BuildContextAsync(userId, question, clock);

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
               to nutrition or wellness — but questions about today's date, day or time are
               fine: answer them from "now".
            3) "now" is the user's local date, day and time. Use it for anything time-related,
               and fit suggestions to the time of day (breakfast in the morning, dinner in the
               evening), taking "mealsLoggedToday" into account.
            4) Earlier messages in this conversation are for context only (e.g. to understand
               "what about lunch?"). For every number, use the CURRENT data below — it may have
               changed since those messages.
            5) Never provide medical diagnosis or treatment advice — this is wellness guidance
               only, and say so if a question strays toward a medical concern.
            6) When suggesting what to eat, only suggest foods listed in "mealSuggestionCandidates"
               or "relevantFoodsFromDatabase", using their listed values (per servingSizeGrams).
               Never invent a dish or its nutrition values. If both lists are empty, say there
               are no suitable options in the food database right now.
            7) Never suggest a food that matches one of the user's "allergies", and respect
               their "dietaryPreferences" (e.g. no meat for Vegetarian, no pork for Halal).
            8) Keep answers concise and encouraging, 2-4 sentences unless asked for detail.
            """;

        var userPrompt =
            $"USER'S CURRENT DATA (use these exact numbers, never estimate your own):\n{structuredContextJson}\n\n" +
            $"RELEVANT NUTRITION KNOWLEDGE (use if relevant to the question):\n{knowledgeText}\n\n" +
            $"USER'S QUESTION:\n{question}";

        // ---- 4. Call Groq, with the conversation so far as memory ----
        var answer = await _groqClient.AskAsync(systemPrompt, userPrompt, history);

        // ---- 5. Persist the conversation, WITH what was retrieved ----
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

        return new ChatResponse(answer, sources, conversation.Id);
    }

    public async Task<Guid> StartNewConversationAsync(Guid userId)
    {
        // Old conversations are kept and stay browsable in the history.
        var conversation = new AiConversation { UserId = userId };
        _db.AiConversations.Add(conversation);
        await _db.SaveChangesAsync();
        return conversation.Id;
    }

    public async Task<List<ConversationSummary>> ListConversationsAsync(Guid userId)
    {
        var rows = await _db.AiConversations
            .Where(c => c.UserId == userId && c.Messages.Any())
            .Select(c => new
            {
                c.Id,
                c.StartedAtUtc,
                Count = c.Messages.Count,
                Last = c.Messages.Max(m => m.CreatedAtUtc),
                FirstQuestion = c.Messages
                    .Where(m => m.Role == AiMessageRole.User)
                    .OrderBy(m => m.CreatedAtUtc)
                    .Select(m => m.Content)
                    .FirstOrDefault(),
            })
            .OrderByDescending(c => c.Last)
            .ToListAsync();

        return rows
            .Select(r => new ConversationSummary(r.Id, TitleFrom(r.FirstQuestion), r.StartedAtUtc, r.Last, r.Count))
            .ToList();
    }

    public async Task<List<ChatHistoryMessage>> GetConversationMessagesAsync(Guid userId, Guid conversationId)
    {
        if (!await _db.AiConversations.AnyAsync(c => c.Id == conversationId && c.UserId == userId))
            throw new KeyNotFoundException("Conversation not found.");

        return await _db.AiMessages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAtUtc)
            .ThenBy(m => m.Role)
            .Select(m => new ChatHistoryMessage(m.Id, m.Role.ToString(), m.Content, m.CreatedAtUtc))
            .ToListAsync();
    }

    public async Task<List<ChatSearchResult>> SearchAsync(Guid userId, string query)
    {
        query = query.Trim();
        if (query.Length < 2) return new List<ChatSearchResult>();
        var lowered = query.ToLower();

        // Case-insensitive "contains" (translated to SQL), newest first.
        var matches = await _db.AiMessages
            .Where(m => m.Conversation!.UserId == userId && m.Content.ToLower().Contains(lowered))
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(30)
            .Select(m => new { m.Id, m.ConversationId, m.Role, m.Content, m.CreatedAtUtc })
            .ToListAsync();

        var conversationIds = matches.Select(m => m.ConversationId).Distinct().ToList();
        var titles = await _db.AiMessages
            .Where(m => conversationIds.Contains(m.ConversationId) && m.Role == AiMessageRole.User)
            .GroupBy(m => m.ConversationId)
            .Select(g => new { g.Key, First = g.OrderBy(m => m.CreatedAtUtc).Select(m => m.Content).First() })
            .ToDictionaryAsync(x => x.Key, x => x.First);

        return matches
            .Select(m => new ChatSearchResult(
                m.ConversationId, TitleFrom(titles.GetValueOrDefault(m.ConversationId)), m.Id,
                m.Role.ToString(), Snippet(m.Content, query), m.CreatedAtUtc))
            .ToList();
    }

    public async Task DeleteConversationAsync(Guid userId, Guid conversationId)
    {
        var conversation = await _db.AiConversations.FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId)
            ?? throw new KeyNotFoundException("Conversation not found.");
        _db.AiConversations.Remove(conversation); // messages cascade
        await _db.SaveChangesAsync();
    }

    // A conversation is named after its first question, shortened.
    private static string TitleFrom(string? firstQuestion)
    {
        var text = (firstQuestion ?? "New chat").ReplaceLineEndings(" ").Trim();
        return text.Length <= 60 ? text : text[..57].TrimEnd() + "...";
    }

    // About 120 characters around the first match, so results show context.
    private static string Snippet(string content, string query)
    {
        var text = content.ReplaceLineEndings(" ");
        var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0 || text.Length <= 120) return text.Length <= 120 ? text : text[..117] + "...";
        var start = Math.Max(0, index - 40);
        var length = Math.Min(120, text.Length - start);
        return (start > 0 ? "..." : "") + text.Substring(start, length) + (start + length < text.Length ? "..." : "");
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
