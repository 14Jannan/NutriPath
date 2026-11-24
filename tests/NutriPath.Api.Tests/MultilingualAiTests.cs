using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Services;
using NutriPath.Api.Services.Llm;
using NutriPath.Api.Services.Multilingual;

namespace NutriPath.Api.Tests;

public class LanguageResolutionTests
{
    private static readonly LanguageResolver Resolver = new();

    [Theory]
    [InlineData("How much protein do I need?", "en")]
    [InlineData("எனக்கு தினமும் எவ்வளவு புரதம் தேவை?", "ta")]
    [InlineData("මට දිනකට කොපමණ ප්‍රෝටීන් අවශ්‍යද?", "si")]
    [InlineData("எனக்கு protein எவ்வளவு வேண்டும்?", "ta")] // mixed English and Tamil
    [InlineData("මට protein කොපමණ ඕනෙද?", "si")]          // mixed English and Sinhala
    [InlineData("enakku evvalavu puratham venum?", "ta")]  // Tanglish
    [InlineData("mata dawasata protein kochchara oneda?", "si")] // Singlish
    public void DetectsLanguageFromScriptAndTransliteration(string question, string expected)
    {
        var result = Resolver.Resolve(question, "auto", null);
        Assert.Equal(expected, result.Code);
        Assert.Equal("detected", result.Source);
    }

    [Fact]
    public void PriorityIsExplicitThenSelectedThenDetectedThenPreferenceThenDefault()
    {
        Assert.Equal(new LanguageResolution("ta", "explicit"), Resolver.Resolve("Explain fibre in Tamil please", "si", "en"));
        Assert.Equal(new LanguageResolution("si", "selected"), Resolver.Resolve("How much protein do I need?", "si", "ta"));
        Assert.Equal(new LanguageResolution("ta", "detected"), Resolver.Resolve("புரதம் என்றால் என்ன?", "auto", "si"));
        Assert.Equal(new LanguageResolution("si", "preference", true), Resolver.Resolve("ok", "auto", "si"));
        Assert.Equal(new LanguageResolution("en", "default", true), Resolver.Resolve("ok", null, null));
    }

    [Theory]
    [InlineData("தமிழில் சொல்லுங்கள்: fibre என்றால் என்ன?", "ta")]
    [InlineData("සිංහලෙන් කියන්න, protein මොකක්ද?", "si")]
    [InlineData("ඉංග්‍රීසියෙන් කියන්න", "en")]
    [InlineData("ஆங்கிலத்தில் பதில் சொல்லுங்கள்", "en")]
    [InlineData("Please answer in Sinhala: what is fibre?", "si")]
    [InlineData("protein pathi tamil la sollunga", "ta")]
    public void HonoursExplicitLanguageRequests(string question, string expected) =>
        Assert.Equal(expected, LanguageResolver.ExplicitRequest(question));

    [Theory]
    [InlineData("What foods are popular in Tamil Nadu?")]
    [InlineData("Is Sinhala food spicy?")]
    public void MentioningALanguageIsNotARequest(string question) =>
        Assert.Null(LanguageResolver.ExplicitRequest(question));

    [Theory]
    [InlineData("ok")]
    [InlineData("and lunch?")]
    [InlineData("venum")]
    public void ShortLatinFollowUpsAreAmbiguous(string question) =>
        Assert.True(LanguageResolver.Detect(question).Ambiguous);

    [Fact]
    public void DisabledMultilingualModeAlwaysAnswersInEnglish()
    {
        var resolver = new LanguageResolver(Options.Create(new AiSettings { Multilingual = { Enabled = false } }));
        Assert.Equal(new LanguageResolution("en", "disabled"), resolver.Resolve("புரதம் என்றால் என்ன?", "ta", "ta"));
    }

    [Fact]
    public void UnsupportedLanguagesInConfigurationAreNeverChosen()
    {
        var resolver = new LanguageResolver(Options.Create(new AiSettings
        {
            Multilingual = { SupportedLanguages = ["en", "ta"] },
        }));
        Assert.Equal("en", resolver.Resolve("How much protein?", "si", "si").Code);
    }

    [Fact]
    public void NormalizationKeepsSinhalaJoinersButMatchKeysIgnoreThem()
    {
        const string withJoiner = "ප්‍රෝටීන්";
        Assert.Contains('‍', Languages.Normalize(withJoiner));
        Assert.Equal(Languages.MatchKey("ප්රෝටීන්"), Languages.MatchKey(withJoiner));
        Assert.Equal("é x", Languages.Normalize("é   x "));
    }
}

public class MultilingualRetrievalTests
{
    [Theory]
    [InlineData("எனக்கு தினமும் எவ்வளவு புரதம் தேவை?", "protein")]
    [InlineData("මට දිනකට කොපමණ ප්‍රෝටීන් අවශ්‍යද?", "protein")]
    [InlineData("මට දිනකට කොපමණ ප්රෝටීන් අවශ්යද?", "protein")] // typed without the joiner
    [InlineData("நார்ச்சத்து உள்ள உணவுகள் எவை?", "fibre")]
    [InlineData("වතුර කොපමණ බොන්න ඕනද?", "water")]
    [InlineData("enakku evvalavu puratham venum", "protein")]
    public void MapsTamilSinhalaAndTransliteratedTermsToEnglish(string question, string expected) =>
        Assert.Contains(NutritionTermLexicon.EnglishTermsFor(question), t => t.Split(' ').Contains(expected));

    [Fact]
    public void EnglishQuestionsAreNotExpanded() =>
        Assert.Equal("How much rice?", NutritionTermLexicon.ExpandForKeywordSearch("How much rice?", "en"));

    [Fact]
    public void UnmappedQuestionsGetNoInventedTerms() =>
        Assert.Empty(NutritionTermLexicon.EnglishTermsFor("கனடாவின் தலைநகரம் எது?")); // "capital of Canada"

    [Theory]
    [InlineData("How much protein do I need?", "Protein sources")]
    [InlineData("எனக்கு தினமும் எவ்வளவு புரதம் தேவை?", "Protein sources")]
    [InlineData("මට දිනකට කොපමණ ප්‍රෝටීන් අවශ්‍යද?", "Protein sources")]
    [InlineData("தண்ணீர் எவ்வளவு குடிக்க வேண்டும்?", "Drinking water and hydration")]
    [InlineData("ලුණු අඩු කරන්නේ කොහොමද?", "Salt and sodium")]
    [InlineData("இடியப்பத்துடன் காலை உணவை எப்படி சமநிலைப்படுத்துவது?", "Sri Lankan breakfast staples")]
    public async Task KeywordFallbackFindsEnglishEvidenceForAllThreeLanguages(string question, string expectedTitle)
    {
        await using var fixture = await ReviewedKnowledge.CreateAsync();
        var results = await fixture.Retrieval.SearchWithModeAsync(question, "keyword", evaluationTopK: 3);
        Assert.Contains(expectedTitle, results.Select(c => c.Document!.Title));
    }

    [Fact]
    public async Task KeywordFallbackReturnsNothingForUnmappedTamilQuestions()
    {
        await using var fixture = await ReviewedKnowledge.CreateAsync();
        Assert.Empty(await fixture.Retrieval.SearchWithModeAsync("கனடாவின் தலைநகரம் எது?", "keyword", evaluationTopK: 3));
    }

    /// <summary>The 24 reviewed English documents in an in-memory database, one chunk each.</summary>
    internal sealed class ReviewedKnowledge : IAsyncDisposable
    {
        private ServiceProvider _services = null!;
        public HybridKnowledgeRetrieval Retrieval { get; private set; } = null!;

        public static async Task<ReviewedKnowledge> CreateAsync()
        {
            var name = Guid.NewGuid().ToString();
            var services = new ServiceCollection()
                .AddDbContext<NutriPathDbContext>(o => o.UseInMemoryDatabase(name))
                .BuildServiceProvider();
            var db = services.GetRequiredService<NutriPathDbContext>();
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "Seed", "reviewed-knowledge.json");
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            foreach (var item in json.RootElement.EnumerateArray())
            {
                var title = item.GetProperty("title").GetString()!;
                var content = item.GetProperty("content").GetString()!;
                db.KnowledgeDocuments.Add(new KnowledgeDocument
                {
                    Title = title, Content = content, Approved = true, Language = "en",
                    SourceUrl = item.GetProperty("sourceUrl").GetString()!, ContentHash = Guid.NewGuid().ToString("N"),
                    Chunks = [new KnowledgeChunk { Title = title, Content = content }],
                });
            }
            await db.SaveChangesAsync();
            var keyword = new KnowledgeRetrievalService(services.GetRequiredService<IServiceScopeFactory>());
            var retrieval = new HybridKnowledgeRetrieval(keyword, db, new DisabledEmbeddings(),
                new ConfigurationBuilder().Build(), NullLogger<HybridKnowledgeRetrieval>.Instance);
            await retrieval.EnsureIndexBuiltAsync();
            return new ReviewedKnowledge { _services = services, Retrieval = retrieval };
        }

        public async ValueTask DisposeAsync() => await _services.DisposeAsync();
    }

    private sealed class DisabledEmbeddings : IEmbeddingService
    {
        public string Model => EmbeddingModels.MultilingualE5Small;
        public bool Enabled => false;
        public Task<float[][]> EmbedAsync(string[] texts, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Embeddings are disabled in this test.");
    }
}

public class MultilingualValidationTests
{
    private static string Reply(string answer, bool insufficient = false) =>
        JsonSerializer.Serialize(new { answer, sourceIds = Array.Empty<Guid>(), insufficientEvidence = insufficient });

    [Fact]
    public void TamilAndSinhalaAnswersWithExactValuesAreAccepted()
    {
        const string context = """{"dailyTargets":{"proteinGrams":113,"calories":2173}}""";
        var tamil = GroundedNutritionAnswer.Parse(Reply("உங்கள் தினசரி புரத இலக்கு **113 g** மற்றும் கலோரி இலக்கு 2,173 kcal."), [], context, "ta");
        Assert.False(tamil.InsufficientEvidence);
        var sinhala = GroundedNutritionAnswer.Parse(Reply("ඔබේ දෛනික ප්‍රෝටීන් ඉලක්කය ග්‍රෑම් 113 නොවේ, 113 ග්‍රෑම් වේ."), [], context, "si");
        Assert.False(sinhala.InsufficientEvidence);
    }

    [Theory]
    [InlineData("ta", "உங்கள் புரத இலக்கு 120 கிராம் ஆகும்.")]
    [InlineData("ta", "உங்கள் புரத இலக்கு 120gக்கு மேல்.")]
    [InlineData("si", "ඔබේ ප්‍රෝටීන් ඉලක්කය 120 ග්‍රෑම් වේ.")]
    [InlineData("si", "ඔබේ ප්රෝටීන් ඉලක්කය 120 ග්රෑම් වේ.")]
    [InlineData("ta", "உங்கள் கலோரி இலக்கு ௨௧௭௩ kcal.")] // Tamil digits can't be checked
    public void ChangedOrUncheckableNumbersInLocalizedAnswersFailClosed(string language, string answer)
    {
        var parsed = GroundedNutritionAnswer.Parse(Reply(answer), [], """{"proteinGrams":113}""", language);
        Assert.True(parsed.InsufficientEvidence);
        Assert.Equal(GroundedNutritionAnswer.Unverified(language), parsed.Answer);
    }

    [Theory]
    [InlineData("ta", "Your protein target comes from your profile and today's log.")]
    [InlineData("si", "உங்கள் புரத இலக்கு உங்கள் சுயவிவரத்திலிருந்து வருகிறது.")]
    [InlineData("en", "ඔබේ ප්‍රෝටීන් ඉලක්කය ඔබේ පැතිකඩෙන් ලැබේ.")]
    public void AnswersInTheWrongLanguageAreRejected(string language, string answer)
    {
        var parsed = GroundedNutritionAnswer.Parse(Reply(answer), [], "{}", language);
        Assert.True(parsed.InsufficientEvidence);
        Assert.Equal(GroundedNutritionAnswer.Unverified(language), parsed.Answer);
    }

    [Fact]
    public void TamilAnswersMayKeepEnglishFoodNamesAndUnits()
    {
        var parsed = GroundedNutritionAnswer.Parse(
            Reply("**Red lentils (dhal)** போன்ற பருப்பு வகைகள் புரதம் மற்றும் நார்ச்சத்தை வழங்குகின்றன."), [], "{}", "ta");
        Assert.False(parsed.InsufficientEvidence);
    }

    [Theory]
    [InlineData("ta", "இந்த உணவு ஒவ்வாமை இல்லாத உணவு, முற்றிலும் பாதுகாப்பானது.")]
    [InlineData("si", "මෙම ආහාරය අසාත්මිකතා රහිත සහ සම්පූර්ණයෙන්ම ආරක්ෂිතයි.")]
    [InlineData("en", "This snack is allergen-free.")]
    public void AllergySafetyGuaranteesAreReplacedInEveryLanguage(string language, string answer)
    {
        var parsed = GroundedNutritionAnswer.Parse(Reply(answer), [], "{}", language);
        Assert.True(parsed.InsufficientEvidence);
        Assert.Equal(GroundedNutritionAnswer.UnverifiedAllergySafety(language), parsed.Answer);
    }

    [Fact]
    public void FabricatedCitationsAreRejectedWithALocalizedMessage()
    {
        var doc = new KnowledgeDocument { Title = "Protein sources", SourceUrl = "https://example.org/p", Approved = true };
        var reply = JsonSerializer.Serialize(new { answer = "புரதம் தசைகளுக்கு உதவுகிறது.", sourceIds = new[] { Guid.NewGuid() }, insufficientEvidence = false });
        var parsed = GroundedNutritionAnswer.Parse(reply, [new KnowledgeChunk { Document = doc }], "{}", "ta");
        Assert.True(parsed.InsufficientEvidence);
        Assert.Equal(GroundedNutritionAnswer.Unverified("ta"), parsed.Answer);
    }
}

public class GeminiClientTests
{
    private static GeminiClient Create(RecordingHandler handler, GeminiSettings? settings = null, List<TimeSpan>? waits = null) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/") },
            Options.Create(settings ?? new GeminiSettings { ApiKey = "test-key" }), NullLogger<GeminiClient>.Instance,
            (wait, _) => { waits?.Add(wait); return Task.CompletedTask; });

    private static LlmRequest Request() => new("System rules", "முதல் கேள்வி",
        [new LlmMessage("user", "Hi"), new LlmMessage("assistant", "Hello")], AiService.AnswerSchema);

    private static HttpResponseMessage Ok(string text, bool thought = false) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new
        {
            candidates = new[]
            {
                new
                {
                    content = new { role = "model", parts = thought
                        ? new object[] { new { text = "private reasoning", thought = true }, new { text } }
                        : new object[] { new { text } } },
                    finishReason = "STOP",
                },
            },
            usageMetadata = new { promptTokenCount = 100, candidatesTokenCount = 20, totalTokenCount = 150 },
            modelVersion = "gemini-3.1-flash-lite",
        }),
    };

    [Fact]
    public async Task SerializesSystemInstructionHistoryAndStructuredOutput()
    {
        var handler = new RecordingHandler(_ => Ok("""{"answer":"ok","sourceIds":[],"insufficientEvidence":false}"""));
        var response = await Create(handler).GenerateAsync(Request());

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.EndsWith("/v1beta/models/gemini-3.5-flash-lite:generateContent", sent.Uri);
        Assert.Equal("test-key", sent.ApiKey);
        Assert.DoesNotContain("test-key", sent.Uri + sent.Body);
        using var body = JsonDocument.Parse(sent.Body);
        var root = body.RootElement;
        Assert.Equal("System rules", root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        var roles = root.GetProperty("contents").EnumerateArray().Select(c => c.GetProperty("role").GetString()!).ToArray();
        Assert.Equal(["user", "model", "user"], roles);
        Assert.Equal("முதல் கேள்வி", root.GetProperty("contents")[2].GetProperty("parts")[0].GetProperty("text").GetString());
        var config = root.GetProperty("generationConfig");
        Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
        Assert.Equal("object", config.GetProperty("responseJsonSchema").GetProperty("type").GetString());
        Assert.Equal("low", config.GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
        Assert.Equal(1024, config.GetProperty("maxOutputTokens").GetInt32());

        Assert.Equal(150, response.TotalTokens);
        Assert.Equal("Gemini", response.Provider);
    }

    [Fact]
    public async Task IgnoresThoughtPartsInTheAnswer()
    {
        var handler = new RecordingHandler(_ => Ok("visible", thought: true));
        Assert.Equal("visible", (await Create(handler).GenerateAsync(Request())).Text);
    }

    [Fact]
    public async Task RetriesTransientFailuresWithBoundedBackoff()
    {
        var calls = 0;
        var handler = new RecordingHandler(_ => ++calls < 3
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") }
            : Ok("done"));
        var waits = new List<TimeSpan>();
        var response = await Create(handler, waits: waits).GenerateAsync(Request());
        Assert.Equal("done", response.Text);
        Assert.Equal(3, calls);
        Assert.Equal(2, waits.Count);
        Assert.All(waits, w => Assert.InRange(w, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(8)));
    }

    [Fact]
    public async Task HonoursShortRetryHintsFromTheErrorBody()
    {
        var calls = 0;
        var handler = new RecordingHandler(_ => ++calls == 1 ? RateLimited("2s") : Ok("done"));
        var waits = new List<TimeSpan>();
        await Create(handler, waits: waits).GenerateAsync(Request());
        Assert.Equal([TimeSpan.FromSeconds(2)], waits);
    }

    [Fact]
    public async Task LongQuotaWaitsFailFastWithRetryAfter()
    {
        var handler = new RecordingHandler(_ => RateLimited("3600s"));
        var error = await Assert.ThrowsAsync<LlmRateLimitedException>(() => Create(handler).GenerateAsync(Request()));
        Assert.Equal(TimeSpan.FromHours(1), error.RetryAfter);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task InvalidRequestsAreNotRetried(HttpStatusCode status)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(status) { Content = new StringContent("{}") });
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Create(handler).GenerateAsync(Request()));
        Assert.Equal(status, error.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SafetyBlocksProduceNoAnswerText()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                candidates = new[] { new { content = new { parts = new[] { new { text = "partial" } } }, finishReason = "SAFETY" } },
            }),
        });
        Assert.Equal("", (await Create(handler).GenerateAsync(Request())).Text);
    }

    [Fact]
    public async Task MissingApiKeyFailsWithoutCallingGoogle()
    {
        var handler = new RecordingHandler(_ => Ok("x"));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Create(handler, new GeminiSettings { ApiKey = "" }).GenerateAsync(Request()));
        Assert.Empty(handler.Requests);
    }

    private static HttpResponseMessage RateLimited(string delay) => new(HttpStatusCode.TooManyRequests)
    {
        Content = new StringContent($$$"""
            {"error":{"code":429,"status":"RESOURCE_EXHAUSTED","details":[{"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"{{{delay}}}"}]}}
            """),
    };

    internal sealed record SentRequest(HttpMethod Method, string Uri, string? ApiKey, string Body);

    internal sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var key = request.Headers.TryGetValues("x-goog-api-key", out var values) ? values.Single() : null;
            Requests.Add(new(request.Method, request.RequestUri!.ToString(), key,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(token)));
            return respond(request);
        }
    }
}

public class ProviderSelectionTests
{
    private static (LlmProviderSelector Selector, NutriPathDbContext Db, ServiceProvider Services) Create(
        AiSettings ai, GeminiSettings? gemini = null)
    {
        var services = new ServiceCollection()
            .AddSingleton<IGroqClient>(new StubGroq())
            .AddSingleton(Options.Create(new GroqSettings()))
            .AddSingleton<GroqLlmClient>()
            .AddSingleton(new GeminiClient(new HttpClient(), Options.Create(gemini ?? new GeminiSettings()),
                NullLogger<GeminiClient>.Instance))
            .BuildServiceProvider();
        var db = TestDb.Create();
        return (new LlmProviderSelector(Options.Create(ai), Options.Create(gemini ?? new GeminiSettings()), db, services), db, services);
    }

    private static User AddUser(NutriPathDbContext db, string email)
    {
        var user = TestDb.AddUser(db);
        user.Email = email;
        db.SaveChanges();
        return user;
    }

    [Fact]
    public async Task GroqRemainsTheDefaultProvider()
    {
        var (selector, db, _) = Create(new AiSettings());
        Assert.Equal("Groq", (await selector.SelectAsync(AddUser(db, "student@uni.lk").Id)).Provider);
    }

    [Fact]
    public async Task FreeTierGeminiServesSyntheticAccounts()
    {
        var (selector, db, _) = Create(new AiSettings { Provider = "Gemini" });
        Assert.Equal("Gemini", (await selector.SelectAsync(AddUser(db, "demo@nutripath.test").Id)).Provider);
    }

    [Fact]
    public async Task FreeTierGeminiRefusesRealUsersWithoutAnExplicitAlternative()
    {
        var (selector, db, _) = Create(new AiSettings { Provider = "Gemini" });
        await Assert.ThrowsAsync<LlmProviderNotPermittedException>(() =>
            selector.SelectAsync(AddUser(db, "student@uni.lk").Id));
    }

    [Fact]
    public async Task RealUsersUseTheExplicitlyConfiguredAlternative()
    {
        var (selector, db, _) = Create(new AiSettings { Provider = "Gemini", UnapprovedUserProvider = "Groq" });
        Assert.Equal("Groq", (await selector.SelectAsync(AddUser(db, "student@uni.lk").Id)).Provider);
    }

    [Fact]
    public async Task ApprovalOrAnAllowlistLetsRealAccountsUseGemini()
    {
        var (approved, db1, _) = Create(new AiSettings { Provider = "Gemini" }, new GeminiSettings { RealUserDataApproved = true });
        Assert.Equal("Gemini", (await approved.SelectAsync(AddUser(db1, "student@uni.lk").Id)).Provider);
        var (listed, db2, _) = Create(new AiSettings { Provider = "Gemini" },
            new GeminiSettings { ApprovedUserEmails = ["tester@uni.lk"] });
        Assert.Equal("Gemini", (await listed.SelectAsync(AddUser(db2, "Tester@Uni.lk").Id)).Provider);
    }

    [Theory]
    [InlineData("demo@nutripath.test", true)]
    [InlineData("demo@NUTRIPATH.TEST", true)]
    [InlineData("demo@nutripath.test.evil.com", false)]
    [InlineData("nutripath.test@gmail.com", false)]
    [InlineData("", false)]
    public void SyntheticDomainMatchingIsExact(string email, bool expected) =>
        Assert.Equal(expected, LlmProviderSelector.IsSyntheticOrApproved(email, new GeminiSettings()));

    private sealed class StubGroq : IGroqClient
    {
        public Task<GroqReply> AskAsync(string systemPrompt, string userMessage, IReadOnlyList<GroqMessage>? history = null) =>
            Task.FromResult(new GroqReply("{}", 1));
    }
}

public class MultilingualChatTests
{
    private sealed class RecordingLlm(Func<LlmRequest, string> reply) : ILlmClient
    {
        public List<LlmRequest> Requests { get; } = [];
        public string Provider => "Gemini";
        public string Model => "gemini-3.1-flash-lite";

        public Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new LlmResponse(reply(request), 500, Provider, Model));
        }
    }

    private sealed class FixedContext : IAiContextBuilder
    {
        public Task<string> BuildContextAsync(Guid userId, string userQuestion, ClientClock clock) =>
            Task.FromResult("""{"dailyTargets":{"proteinGrams":113}}""");
    }

    private sealed class NoRetrieval : IKnowledgeRetrievalService
    {
        public Task EnsureIndexBuiltAsync() => Task.CompletedTask;
        public List<KnowledgeChunk> Search(string query, int topK) => [];
    }

    private static readonly ClientClock Clock =
        new(new DateOnly(2026, 11, 24), new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.FromHours(5.5)));

    private static string Json(string answer) =>
        JsonSerializer.Serialize(new { answer, sourceIds = Array.Empty<Guid>(), insufficientEvidence = false });

    private static AiService Service(NutriPathDbContext db, ILlmClient llm) =>
        new(new FixedContext(), new NoRetrieval(), new FixedLlmProviderSelector(llm), new LanguageResolver(),
            Options.Create(new AiSettings()), db, AiServiceTests.Protector);

    [Fact]
    public async Task TamilQuestionGetsATamilAnswerAndTheLanguageIsAudited()
    {
        await using var db = TestDb.Create();
        var user = TestDb.AddUser(db);
        var llm = new RecordingLlm(_ => Json("உங்கள் தினசரி புரத இலக்கு **113 g** ஆகும். 💪"));

        var response = await Service(db, llm).ChatAsync(user.Id, "எனக்கு தினமும் எவ்வளவு புரதம் தேவை?", Clock);

        Assert.Equal("ta", response.Language);
        Assert.Equal("detected", response.LanguageSource);
        Assert.Equal("Gemini", response.Provider);
        Assert.False(response.InsufficientEvidence);
        var request = Assert.Single(llm.Requests);
        Assert.Contains("RESPONSE LANGUAGE: Tamil", request.UserMessage);
        Assert.Contains("Write the answer in Tamil.", request.SystemPrompt);
        Assert.Contains("digits 0-9", request.SystemPrompt);
        Assert.NotNull(request.JsonSchema);

        var stored = await db.AiMessages.Where(m => m.Role == AiMessageRole.Assistant).SingleAsync();
        Assert.DoesNotContain("புரத", stored.Content); // encrypted at rest
        Assert.Contains("\"language\":\"ta\"", AiServiceTests.Protector.Unprotect(stored.RetrievedContextJson!));
    }

    [Fact]
    public async Task SelectedLanguageWinsAndIsSavedAsThePreference()
    {
        await using var db = TestDb.Create();
        var user = TestDb.AddUser(db);
        var llm = new RecordingLlm(_ => Json("ඔබේ දෛනික ප්‍රෝටීන් ඉලක්කය 113 g වේ."));
        var service = Service(db, llm);

        var first = await service.ChatAsync(user.Id, "How much protein do I need?", Clock, language: "si");
        Assert.Equal(("si", "selected"), (first.Language, first.LanguageSource));
        Assert.Equal("si", (await db.UserProfiles.SingleAsync()).PreferredLanguage);

        var followUp = await service.ChatAsync(user.Id, "ok", Clock, first.ConversationId, language: "auto");
        Assert.Equal(("si", "preference"), (followUp.Language, followUp.LanguageSource));
    }

    [Fact]
    public async Task WrongLanguageRepliesAreReplacedWithTheLocalizedFallback()
    {
        await using var db = TestDb.Create();
        var user = TestDb.AddUser(db);
        var llm = new RecordingLlm(_ => Json("Your protein target is 113 g today, keep it up."));

        var response = await Service(db, llm).ChatAsync(user.Id, "புரதம் எவ்வளவு தேவை?", Clock);

        Assert.True(response.InsufficientEvidence);
        Assert.Equal(GroundedNutritionAnswer.Unverified("ta"), response.Answer);
    }

    [Fact]
    public async Task HistoryInOtherLanguagesIsPassedThroughUntranslated()
    {
        await using var db = TestDb.Create();
        var user = TestDb.AddUser(db);
        var replies = new Queue<string>([Json("Your protein target is 113 g."), Json("உங்கள் இலக்கு 113 g.")]);
        var llm = new RecordingLlm(_ => replies.Dequeue());
        var service = Service(db, llm);

        var first = await service.ChatAsync(user.Id, "What is my protein target?", Clock);
        await service.ChatAsync(user.Id, "அதை தமிழில் சொல்லுங்கள்", Clock, first.ConversationId);

        var history = llm.Requests[1].History;
        Assert.Equal(["What is my protein target?", "Your protein target is 113 g."], history.Select(m => m.Content));
        Assert.Contains("RESPONSE LANGUAGE: Tamil", llm.Requests[1].UserMessage);
    }

    [Fact]
    public async Task ProviderIsChosenBeforePersonalContextIsBuilt()
    {
        await using var db = TestDb.Create();
        var user = TestDb.AddUser(db);
        var context = new CountingContext();
        var service = new AiService(context, new NoRetrieval(), new RefusingSelector(), new LanguageResolver(),
            Options.Create(new AiSettings()), db, AiServiceTests.Protector);

        await Assert.ThrowsAsync<LlmProviderNotPermittedException>(() => service.ChatAsync(user.Id, "Hello", Clock));
        Assert.Equal(0, context.Calls);
        Assert.Empty(db.AiMessages);
    }

    private sealed class CountingContext : IAiContextBuilder
    {
        public int Calls { get; private set; }
        public Task<string> BuildContextAsync(Guid userId, string userQuestion, ClientClock clock)
        {
            Calls++;
            return Task.FromResult("{}");
        }
    }

    private sealed class RefusingSelector : ILlmProviderSelector
    {
        public Task<ILlmClient> SelectAsync(Guid userId, CancellationToken cancellationToken = default) =>
            throw new LlmProviderNotPermittedException("test");
    }
}

public class MultilingualChatEndpointTests
{
    private static async Task<(HttpClient Client, IServiceScope Scope, NutriPathDbContext Db)> CreateAsync(
        NutriPathApiFactory factory, ILlmProviderSelector selector)
    {
        var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddScoped<IAiContextBuilder>(_ => new EmptyContext());
            services.AddScoped(_ => selector);
        }));
        var client = configured.CreateClient();
        var scope = configured.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();
        var user = TestDb.AddUser(db);
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().CreateAccessToken(user).token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        await Task.CompletedTask;
        return (client, scope, db);
    }

    [Fact]
    public async Task UnsupportedLanguageCodesAreRejected()
    {
        await using var factory = new NutriPathApiFactory();
        var (client, scope, _) = await CreateAsync(factory, new ThrowingSelector(new LlmProviderNotPermittedException("x")));
        using (scope)
        {
            var response = await client.PostAsJsonAsync("/api/ai/chat", new { question = "Hi", language = "fr" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task PrivacyGuardRefusalIsReportedWithoutStoringAnything()
    {
        await using var factory = new NutriPathApiFactory();
        var (client, scope, db) = await CreateAsync(factory, new ThrowingSelector(new LlmProviderNotPermittedException("x")));
        using (scope)
        {
            var response = await client.PostAsJsonAsync("/api/ai/chat", new { question = "புரதம்?", language = "ta" });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains("provider_not_permitted", await response.Content.ReadAsStringAsync());
            Assert.Empty(db.AiMessages);
            Assert.Empty(db.AiUsageRecords);
        }
    }

    [Fact]
    public async Task ProviderRateLimitsReturnRetryAfter()
    {
        await using var factory = new NutriPathApiFactory();
        var (client, scope, _) = await CreateAsync(factory, new FixedLlmProviderSelector(
            new ThrowingLlm(new LlmRateLimitedException(TimeSpan.FromSeconds(42)))));
        using (scope)
        {
            var response = await client.PostAsJsonAsync("/api/ai/chat", new { question = "How much protein?" });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(42), response.Headers.RetryAfter?.Delta);
            Assert.Equal("""{"message":"The assistant is temporarily unavailable. Please try again."}""",
                await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public void ChatGateAllowsOneRequestPerUserAtATime()
    {
        var gate = new ChatRequestGate();
        var user = Guid.NewGuid();
        var first = gate.TryEnter(user);
        Assert.NotNull(first);
        Assert.Null(gate.TryEnter(user));
        Assert.NotNull(gate.TryEnter(Guid.NewGuid()));
        first!.Dispose();
        first.Dispose();
        Assert.NotNull(gate.TryEnter(user));
    }

    private sealed class EmptyContext : IAiContextBuilder
    {
        public Task<string> BuildContextAsync(Guid userId, string userQuestion, ClientClock clock) => Task.FromResult("{}");
    }

    private sealed class ThrowingSelector(Exception error) : ILlmProviderSelector
    {
        public Task<ILlmClient> SelectAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromException<ILlmClient>(error);
    }

    private sealed class ThrowingLlm(Exception error) : ILlmClient
    {
        public string Provider => "Gemini";
        public string Model => "test";
        public Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<LlmResponse>(error);
    }
}
