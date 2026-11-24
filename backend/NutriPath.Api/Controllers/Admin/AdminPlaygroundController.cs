using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Authorization;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Models.Admin;
using NutriPath.Api.Services;
using NutriPath.Api.Services.Admin;
using NutriPath.Api.Services.Llm;
using NutriPath.Api.Services.Multilingual;

namespace NutriPath.Api.Controllers.Admin;

public sealed record PlaygroundRequest(string Question, string? Language, string? RetrievalMode, int TopK,
    string? Profile, IReadOnlyList<string>? Providers, bool Generate = true);

/// <summary>
/// Runs the real RAG pipeline step by step on a fictional profile: language resolution →
/// embedding → retrieval → prompt → Gemini and/or Groq → validation. No real user data, history
/// or conversations are used, so synthetic-only providers (free-tier Gemini) are allowed here.
/// </summary>
[Route("api/admin/playground")]
public sealed class AdminPlaygroundController(NutriPathDbContext db, HybridKnowledgeRetrieval retrieval,
    KnowledgeRetrievalService keyword, IEmbeddingService embeddings, ILanguageResolver languages, IServiceProvider services,
    AdminAudit audit) : AdminControllerBase
{
    /// <summary>Fictional profiles. Values are invented and match no real person.</summary>
    public static readonly IReadOnlyDictionary<string, object> Profiles = new Dictionary<string, object>
    {
        ["vegetarian-student"] = new
        {
            profile = "Synthetic vegetarian student (peanut allergy)",
            now = new { date = "Tuesday, 24 November 2026", partOfDay = "evening", localTime = "7:30 PM" },
            mealsLoggedToday = new[] { "Breakfast", "Lunch" },
            todaysLog = new object[]
            {
                new { meal = "Breakfast", foods = new[] { new { name = "String hoppers", grams = 150, calories = 260, proteinGrams = 4.5 } } },
                new { meal = "Lunch", foods = new[] { new { name = "Red rice", grams = 200, calories = 330, proteinGrams = 7.0 }, new { name = "Dhal curry", grams = 150, calories = 210, proteinGrams = 12.0 } } },
            },
            loggingStreak = new { currentDays = 6, bestDays = 9, loggedToday = true },
            userGoal = "Maintain", allergies = new[] { "Peanuts" }, dietaryPreferences = new[] { "Vegetarian" },
            dailyTargets = new { calories = 2173, proteinGrams = 113, fiberGrams = 30 },
            todaySoFar = new { caloriesEaten = 800, caloriesRemaining = 1373, proteinGrams = 23.5, fiberGrams = 11 },
            mealSuggestionCandidates = new[]
            {
                new { name = "Chickpea curry", servingSizeGrams = 150, calories = 240, proteinGrams = 11.0, fiberGrams = 9.0 },
                new { name = "Gotukola sambol", servingSizeGrams = 50, calories = 45, proteinGrams = 1.5, fiberGrams = 2.0 },
            },
            relevantFoodsFromDatabase = Array.Empty<object>(),
        },
        ["athlete"] = new
        {
            profile = "Synthetic university athlete (no allergies)",
            now = new { date = "Tuesday, 24 November 2026", partOfDay = "morning", localTime = "7:00 AM" },
            mealsLoggedToday = Array.Empty<string>(), todaysLog = Array.Empty<object>(),
            loggingStreak = new { currentDays = 2, bestDays = 14, loggedToday = false },
            userGoal = "Gain", allergies = Array.Empty<string>(), dietaryPreferences = Array.Empty<string>(),
            dailyTargets = new { calories = 3050, proteinGrams = 150, fiberGrams = 38 },
            todaySoFar = new { caloriesEaten = 0, caloriesRemaining = 3050, proteinGrams = 0, fiberGrams = 0 },
            mealSuggestionCandidates = new[]
            {
                new { name = "Boiled eggs", servingSizeGrams = 100, calories = 155, proteinGrams = 13, fiberGrams = 0 },
                new { name = "Kiribath", servingSizeGrams = 150, calories = 290, proteinGrams = 5, fiberGrams = 1 },
            },
            relevantFoodsFromDatabase = Array.Empty<object>(),
        },
    };

    [HttpGet("profiles")]
    [AdminPermission(Permissions.AiEvaluate)]
    public IActionResult ListProfiles() => Ok(Profiles.Select(p => new { id = p.Key, profile = p.Value }));

    [HttpPost("run")]
    [AdminPermission(Permissions.AiEvaluate)]
    [EnableRateLimiting(RateLimitPolicies.AiInsight)]
    public async Task<IActionResult> Run([FromBody] PlaygroundRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 1000)
            return BadRequest(new ProblemDetails { Status = 400, Title = "Enter a question (up to 1,000 characters)." });
        var mode = request.RetrievalMode is "keyword" or "semantic" or "hybrid" ? request.RetrievalMode : "hybrid";
        var topK = Math.Clamp(request.TopK <= 0 ? 3 : request.TopK, 1, 8);
        if (!Profiles.TryGetValue(request.Profile ?? "vegetarian-student", out var profile))
            return BadRequest(new ProblemDetails { Status = 400, Title = "Unknown synthetic profile." });
        var steps = new List<object>();
        var total = Stopwatch.StartNew();

        // 1. Language
        var sw = Stopwatch.StartNew();
        var detected = LanguageResolver.Detect(request.Question);
        var resolved = languages.Resolve(request.Question, request.Language, null);
        steps.Add(new { step = "language", ms = sw.Elapsed.TotalMilliseconds, detected = detected.Language, ambiguous = detected.Ambiguous, resolved = resolved.Code, source = resolved.Source });

        // 2. Embedding and nearest neighbours (only when vector search is available)
        var questionLanguage = detected.Language ?? Languages.English;
        object? embeddingStep = null;
        if (mode != "keyword" && embeddings.Enabled && db.Database.IsNpgsql())
        {
            sw.Restart();
            try
            {
                var semanticQuery = retrieval.SemanticQuery(request.Question, questionLanguage);
                var vector = (await embeddings.EmbedAsync([semanticQuery], EmbeddingInputType.Query, ct))[0];
                var embedMs = sw.Elapsed.TotalMilliseconds;
                var nearest = await KnowledgeVectors.NearestWithDistanceAsync(db, vector, embeddings.Model, 8);
                var titles = await db.KnowledgeChunks.AsNoTracking().Where(c => nearest.Select(n => n.ChunkId).Contains(c.Id))
                    .ToDictionaryAsync(c => c.Id, c => c.Title, ct);
                var threshold = EmbeddingModels.MaxDistance(HttpContext.RequestServices.GetRequiredService<IConfiguration>(), embeddings.Model, questionLanguage != Languages.English);
                embeddingStep = new
                {
                    step = "embedding", ms = embedMs, model = embeddings.Model, dimensions = vector.Length, embeddedText = semanticQuery,
                    threshold, nearest = nearest.Select(n => new { n.ChunkId, title = titles.GetValueOrDefault(n.ChunkId), distance = Math.Round(n.Distance, 4), withinThreshold = n.Distance <= threshold }),
                };
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or Npgsql.NpgsqlException or TaskCanceledException)
            {
                embeddingStep = new { step = "embedding", ms = sw.Elapsed.TotalMilliseconds, error = "Embedding or vector search unavailable; keyword fallback applies." };
            }
        }
        else embeddingStep = new { step = "embedding", skipped = mode == "keyword" ? "Keyword mode" : "Vector search not available in this environment" };
        steps.Add(embeddingStep);

        // 3. Retrieval
        sw.Restart();
        await retrieval.EnsureIndexBuiltAsync();
        var chunks = await retrieval.SearchWithModeAsync(request.Question, mode, evaluationTopK: topK);
        var retrievalMs = sw.Elapsed.TotalMilliseconds;
        var keywordOrder = keyword.Search(NutritionTermLexicon.ExpandForKeywordSearch(request.Question, questionLanguage), 8, 0)
            .Select((c, i) => new { rank = i + 1, c.Title }).ToList();
        steps.Add(new
        {
            step = "retrieval", ms = retrievalMs, mode, topK,
            mappedTerms = NutritionTermLexicon.EnglishTermsFor(request.Question),
            keywordRanking = keywordOrder,
            chunks = chunks.Select((c, i) => new
            {
                rank = i + 1, c.Id, c.Title, documentId = c.DocumentId, source = c.Document?.Organization, url = c.Document?.SourceUrl,
                excerpt = c.Content.Length > 300 ? c.Content[..300] + "…" : c.Content,
            }),
        });

        // 4. Prompt
        var context = JsonSerializer.Serialize(profile);
        var system = AiPrompt.System(resolved.Code, true);
        var user = AiPrompt.User(context, chunks, resolved.Code, request.Question);
        steps.Add(new { step = "prompt", systemPromptCharacters = system.Length, userPromptCharacters = user.Length, systemPrompt = system, userPrompt = user });

        // 5–6. Generation and validation per provider
        var results = new List<object>();
        if (request.Generate)
        {
            foreach (var provider in (request.Providers is { Count: > 0 } p ? p : [AiSettings.Gemini]).Distinct().Take(2))
            {
                ILlmClient? client = provider switch
                {
                    AiSettings.Gemini => services.GetRequiredService<GeminiClient>(),
                    AiSettings.Groq => services.GetRequiredService<GroqLlmClient>(),
                    _ => null,
                };
                if (client is null) continue;
                sw.Restart();
                try
                {
                    var reply = await client.GenerateAsync(new LlmRequest(system, user, [], AiService.AnswerSchema), ct);
                    var latency = sw.Elapsed.TotalMilliseconds;
                    var grounded = GroundedNutritionAnswer.Parse(reply.Text, chunks, context, resolved.Code);
                    string? rawAnswer = null;
                    try
                    {
                        rawAnswer = JsonDocument.Parse(reply.Text).RootElement.GetProperty("answer").GetString();
                    }
                    catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
                    results.Add(new
                    {
                        provider = client.Provider, model = reply.Model, ms = latency, tokens = reply.TotalTokens, inputTokens = reply.InputTokens, outputTokens = reply.OutputTokens,
                        validJson = rawAnswer is not null,
                        validationPassed = grounded.Answer != GroundedNutritionAnswer.Unverified(resolved.Code) && grounded.Answer != GroundedNutritionAnswer.UnverifiedAllergySafety(resolved.Code),
                        languageCorrect = rawAnswer is not null && GroundedNutritionAnswer.IsInLanguage(rawAnswer, resolved.Code),
                        allergyGuarantee = rawAnswer is not null && GroundedNutritionAnswer.GuaranteesAllergySafety(rawAnswer),
                        grounded.InsufficientEvidence, citations = grounded.References, answer = grounded.Answer, rawAnswer,
                    });
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    results.Add(new { provider, ms = sw.Elapsed.TotalMilliseconds, error = ex is LlmRateLimitedException ? "Rate limited" : ex is HttpRequestException { StatusCode: { } code } ? $"HTTP {(int)code}" : "Unavailable" });
                }
            }
        }
        await audit.WriteAsync("playground.run", resourceType: "Playground", summary: $"{mode}, top {topK}, {resolved.Code}, providers: {string.Join(",", request.Providers ?? [])}", cancellationToken: ct);
        return Ok(new { totalMs = total.Elapsed.TotalMilliseconds, steps, generations = results, synthetic = true });
    }
}
