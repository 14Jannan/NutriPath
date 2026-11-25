using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NutriPath.Api.Data;
using NutriPath.Api.DTOs;
using NutriPath.Api.Models;
using NutriPath.Api.Services.Llm;
using NutriPath.Api.Services.Multilingual;

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
    private readonly ILlmProviderSelector _providers;
    private readonly ILanguageResolver _languages;
    private readonly AiSettings _settings;
    private readonly NutriPathDbContext _db;
    // Chat text is private, so it's encrypted before it reaches the
    // database and decrypted only when shown to its owner or sent to the AI.
    private readonly IMessageProtector _protector;
    private readonly Admin.RuntimeSettingsStore? _runtime;
    private readonly Admin.PlatformSettingsStore? _platform;

    public AiService(
        IAiContextBuilder contextBuilder,
        IKnowledgeRetrievalService retrieval,
        ILlmProviderSelector providers,
        ILanguageResolver languages,
        IOptions<AiSettings> settings,
        NutriPathDbContext db,
        IMessageProtector protector,
        Admin.RuntimeSettingsStore? runtime = null, Admin.PlatformSettingsStore? platform = null)
    {
        _runtime = runtime;
        _platform = platform;
        _contextBuilder = contextBuilder;
        _retrieval = retrieval;
        _providers = providers;
        _languages = languages;
        _settings = settings.Value;
        _db = db;
        _protector = protector;
    }

    /// <summary>Single-provider setup with the existing Groq client (used by tests and tools).</summary>
    public AiService(
        IAiContextBuilder contextBuilder,
        IKnowledgeRetrievalService retrieval,
        IGroqClient groqClient,
        NutriPathDbContext db,
        IMessageProtector protector)
        : this(contextBuilder, retrieval, new FixedLlmProviderSelector(new GroqLlmClient(groqClient, new GroqSettings().Model)),
            new LanguageResolver(), Options.Create(new AiSettings()), db, protector)
    {
    }

    /// <summary>The JSON shape every answer must follow; property names stay English in every language.</summary>
    public static readonly object AnswerSchema = new
    {
        type = "object",
        properties = new
        {
            answer = new { type = "string", description = "The reply shown to the user, in the requested language." },
            sourceIds = new
            {
                type = "array",
                description = "GUIDs of retrieved documents that support the answer.",
                items = new { type = "string" },
            },
            insufficientEvidence = new { type = "boolean" },
        },
        required = new[] { "answer", "sourceIds", "insufficientEvidence" },
    };

    // How many earlier messages are sent to the AI as memory: enough for
    // natural follow-ups ("what about lunch?"), small enough to stay fast.
    private const int HistoryMessages = 10;
    private const int HistoryMessageMaxChars = 1500;

    public async Task<ChatResponse> ChatAsync(Guid userId, string question, ClientClock clock, Guid? conversationId = null,
        string? language = null, CancellationToken cancellationToken = default)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        // ---- Language: explicit request > chat selection > detection > saved preference > default ----
        var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        var resolved = _languages.Resolve(question, language, profile?.PreferredLanguage);
        var selected = Languages.NormalizeCode(language);
        if (profile != null && selected != null && profile.PreferredLanguage != selected)
            profile.PreferredLanguage = selected; // saved with the conversation below

        // ---- Provider: decided before any personal data is gathered for the prompt ----
        var llm = await _providers.SelectAsync(userId, cancellationToken);

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
            ? new List<LlmMessage>()
            : (await _db.AiMessages
                    .Where(m => m.ConversationId == conversation.Id)
                    .OrderByDescending(m => m.CreatedAtUtc)
                    .ThenByDescending(m => m.Role)
                    .Take(HistoryMessages)
                    .Select(m => new { m.Role, m.Content })
                    .ToListAsync())
                .AsEnumerable()
                .Reverse()
                .Select(m => new { m.Role, Text = _protector.Unprotect(m.Content) })
                .Select(m => new LlmMessage(
                    m.Role == AiMessageRole.User ? "user" : "assistant",
                    m.Text.Length > HistoryMessageMaxChars ? m.Text[..HistoryMessageMaxChars] : m.Text))
                .ToList();

        // ---- 1. STRUCTURED retrieval: exact facts, plain EF Core queries ----
        var structuredContextJson = await _contextBuilder.BuildContextAsync(userId, question, clock);

        // ---- 2. SEMANTIC retrieval: relevant knowledge, similarity search ----
        var retrievalStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        await _retrieval.EnsureIndexBuiltAsync();
        var relevantChunks = await _retrieval.SearchAsync(question);
        var retrievalMs = (int)System.Diagnostics.Stopwatch.GetElapsedTime(retrievalStarted).TotalMilliseconds;

        // ---- 3. Build the grounded prompt (shared with the evaluation CLI) ----
        var systemPrompt = AiPrompt.System(resolved.Code, _settings.Multilingual.Enabled, _platform?.Prompt.Doc.Guidance);
        var userPrompt = AiPrompt.User(structuredContextJson, relevantChunks, resolved.Code, question);

        // ---- 4. Call the model, with the conversation so far as memory ----
        var reply = await llm.GenerateAsync(new LlmRequest(systemPrompt, userPrompt, history, AnswerSchema), cancellationToken);
        var grounded = GroundedNutritionAnswer.Parse(reply.Text, relevantChunks, structuredContextJson, resolved.Code);
        var answer = grounded.Answer;

        // ---- 5. Persist the conversation, WITH what was retrieved ----
        if (conversation == null)
        {
            conversation = new AiConversation { UserId = userId };
            _db.AiConversations.Add(conversation);
        }

        var sources = grounded.References.Select(c => c.Title).ToList();

        _db.AiMessages.Add(new AiMessage
        {
            ConversationId = conversation.Id,
            Role = AiMessageRole.User,
            Content = _protector.Protect(question),
        });
        var answerMessage = new AiMessage
        {
            ConversationId = conversation.Id,
            Role = AiMessageRole.Assistant,
            Content = _protector.Protect(answer),
            // Stored so any answer can later be audited against the exact
            // grounding data and knowledge chunks the model was given. It
            // holds the user's health data too, so it's encrypted as well.
            RetrievedContextJson = _protector.Protect(JsonSerializer.Serialize(new
            {
                structuredContext = JsonDocument.Parse(structuredContextJson).RootElement,
                knowledgeChunks = relevantChunks.Select(c => c.Id),
                grounded.References,
                grounded.InsufficientEvidence,
                language = resolved.Code,
                languageSource = resolved.Source,
                provider = reply.Provider,
                model = reply.Model,
            })),
        };
        _db.AiMessages.Add(answerMessage);
        // The model tried to promise allergy safety; the user got the safe fallback. Reviewers are told.
        if (answer == GroundedNutritionAnswer.UnverifiedAllergySafety(resolved.Code))
            Admin.QualityService.RaiseBlockedAllergyGuarantee(_db, resolved.Code, reply.Provider, reply.Model);

        _db.AiUsageRecords.Add(new AiUsageRecord { UserId = userId, Tokens = reply.TotalTokens });
        // Operational metadata for the admin portal; no prompt or answer text.
        _db.AiRequestMetrics.Add(new Models.Admin.AiRequestMetric
        {
            UserId = userId, Provider = reply.Provider, Model = reply.Model.Length > 60 ? reply.Model[..60] : reply.Model,
            Language = resolved.Code, TotalTokens = reply.TotalTokens, InputTokens = reply.InputTokens, OutputTokens = reply.OutputTokens,
            LatencyMs = (int)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, RetrievalMs = retrievalMs,
            Outcome = "Success", ValidationPassed = grounded.Answer != GroundedNutritionAnswer.Unverified(resolved.Code) &&
                grounded.Answer != GroundedNutritionAnswer.UnverifiedAllergySafety(resolved.Code),
            Citations = grounded.References.Count, InsufficientEvidence = grounded.InsufficientEvidence,
            ConfigVersion = _runtime?.Ai.Version ?? 0,
        });

        await _db.SaveChangesAsync(cancellationToken);

        return new ChatResponse(answer, sources, conversation.Id,
            References: grounded.References, InsufficientEvidence: grounded.InsufficientEvidence,
            Language: resolved.Code, LanguageSource: resolved.Source, Provider: reply.Provider, MessageId: answerMessage.Id);
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
            .Select(r => new ConversationSummary(r.Id, TitleFrom(Decrypt(r.FirstQuestion)), r.StartedAtUtc, r.Last, r.Count))
            .ToList();
    }

    public async Task<List<ChatHistoryMessage>> GetConversationMessagesAsync(Guid userId, Guid conversationId)
    {
        if (!await _db.AiConversations.AnyAsync(c => c.Id == conversationId && c.UserId == userId))
            throw new KeyNotFoundException("Conversation not found.");

        return await ReadMessagesAsync(conversationId);
    }

    public async Task<List<ChatSearchResult>> SearchAsync(Guid userId, string query)
    {
        query = query.Trim();
        if (query.Length < 2) return new List<ChatSearchResult>();

        // Messages are encrypted, so the database can't search their text.
        // Instead only THIS user's messages are loaded and searched after
        // decrypting — fine at one person's chat volume, and nobody else's
        // messages are ever decrypted for them.
        var messages = (await _db.AiMessages
                .Where(m => m.Conversation!.UserId == userId)
                .OrderByDescending(m => m.CreatedAtUtc)
                .Select(m => new { m.Id, m.ConversationId, m.Role, m.Content, m.CreatedAtUtc })
                .ToListAsync())
            .Select(m => new { m.Id, m.ConversationId, m.Role, Text = _protector.Unprotect(m.Content), m.CreatedAtUtc })
            .ToList();

        // Each conversation's title is its earliest question.
        var titles = messages
            .Where(m => m.Role == AiMessageRole.User)
            .GroupBy(m => m.ConversationId)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.CreatedAtUtc).First().Text);

        return messages
            .Where(m => m.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(30)
            .Select(m => new ChatSearchResult(
                m.ConversationId, TitleFrom(titles.GetValueOrDefault(m.ConversationId)), m.Id,
                m.Role.ToString(), Snippet(m.Text, query), m.CreatedAtUtc))
            .ToList();
    }

    public async Task DeleteConversationAsync(Guid userId, Guid conversationId)
    {
        var conversation = await _db.AiConversations.FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId)
            ?? throw new KeyNotFoundException("Conversation not found.");
        _db.AiConversations.Remove(conversation); // messages cascade
        await _db.SaveChangesAsync();
    }

    private string? Decrypt(string? stored) => stored == null ? null : _protector.Unprotect(stored);

    // Both messages of a turn get almost the same timestamp, so Role
    // breaks ties to keep each question ahead of its answer.
    private async Task<List<ChatHistoryMessage>> ReadMessagesAsync(Guid conversationId) =>
        (await _db.AiMessages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAtUtc)
            .ThenBy(m => m.Role)
            .Select(m => new { m.Id, m.Role, m.Content, m.CreatedAtUtc, m.RetrievedContextJson })
            .ToListAsync())
        .Select(m =>
        {
            var evidence = ReadEvidence(m.RetrievedContextJson);
            return new ChatHistoryMessage(m.Id, m.Role.ToString(), _protector.Unprotect(m.Content), m.CreatedAtUtc,
                evidence.References, evidence.InsufficientEvidence);
        })
        .ToList();

    private (List<KnowledgeSource>? References, bool InsufficientEvidence) ReadEvidence(string? encrypted)
    {
        if (encrypted is null) return (null, false);
        try
        {
            using var context = JsonDocument.Parse(_protector.Unprotect(encrypted));
            var root = context.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, false);
            var references = root.TryGetProperty("References", out var list)
                ? list.Deserialize<List<KnowledgeSource>>() : null;
            var insufficient = root.TryGetProperty("InsufficientEvidence", out var flag) && flag.ValueKind == JsonValueKind.True;
            return (references, insufficient);
        }
        catch (Exception ex) when (ex is JsonException or System.Security.Cryptography.CryptographicException)
        {
            return (null, false);
        }
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

        return await ReadMessagesAsync(conversationId.Value);
    }
}
