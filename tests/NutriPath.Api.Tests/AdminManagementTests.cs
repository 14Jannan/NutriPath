using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Models.Admin;
using NutriPath.Api.Services;
using NutriPath.Api.Services.Admin;
using NutriPath.Api.Services.Llm;

namespace NutriPath.Api.Tests;

public class AdminCredentialTests
{
    [Fact]
    public async Task ReplacingAKeyTestsItEncryptsItAndNeverReturnsIt()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("super@nutripath.test", AdminRoles.SuperAdmin);
        var admin = await h.SignInAsync("super@nutripath.test");
        var store = h.Factory.Services.GetRequiredService<IProviderCredentialStore>();
        Assert.Equal("env-groq-key-0001", await store.GetActiveCredentialAsync(ExternalProviders.Groq, default));

        var replaced = await admin.Client.PutAsJsonAsync("/api/admin/credentials/groq", new { apiKey = "good-groq-key-123456" });
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.Equal("good-groq-key-123456", await store.GetActiveCredentialAsync(ExternalProviders.Groq, default));

        var listing = await admin.Client.GetStringAsync("/api/admin/credentials");
        Assert.DoesNotContain("good-groq-key-123456", listing);
        Assert.Contains("••••3456", listing);

        using var scope = h.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();
        var row = Assert.Single(db.ProviderCredentials);
        Assert.DoesNotContain("good-groq", System.Text.Encoding.UTF8.GetString(row.Ciphertext));
        Assert.Equal(12, row.Nonce.Length);
        Assert.DoesNotContain("good-groq-key-123456", JsonSerializer.Serialize(db.AdminAuditLogs.ToList()));
    }

    [Fact]
    public async Task InvalidCandidatesNeverReplaceAWorkingKey()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("super@nutripath.test", AdminRoles.SuperAdmin);
        var admin = await h.SignInAsync("super@nutripath.test");
        await admin.Client.PutAsJsonAsync("/api/admin/credentials/gemini", new { apiKey = "good-gemini-key-0001" });

        var bad = await admin.Client.PutAsJsonAsync("/api/admin/credentials/gemini", new { apiKey = "bad-gemini-key-0002" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        var limited = await admin.Client.PutAsJsonAsync("/api/admin/credentials/gemini", new { apiKey = "rate-gemini-key-0003" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, limited.StatusCode);
        Assert.Equal("good-gemini-key-0001",
            await h.Factory.Services.GetRequiredService<IProviderCredentialStore>().GetActiveCredentialAsync(ExternalProviders.Gemini, default));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.PutAsJsonAsync("/api/admin/credentials/gemini", new { apiKey = "short" })).StatusCode);
    }

    [Fact]
    public async Task RevokedKeysAreNotReactivatedAndEnvironmentKeysDoNotComeBack()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("super@nutripath.test", AdminRoles.SuperAdmin);
        var admin = await h.SignInAsync("super@nutripath.test");
        await admin.Client.PutAsJsonAsync("/api/admin/credentials/groq", new { apiKey = "good-groq-key-000001" });
        await admin.Client.PutAsJsonAsync("/api/admin/credentials/groq", new { apiKey = "good-groq-key-000002" });
        using var scope = h.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();
        var v1 = db.ProviderCredentials.Single(c => c.Version == 1);
        var v2 = db.ProviderCredentials.Single(c => c.Version == 2);

        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync($"/api/admin/credentials/groq/rollback/{v1.Id}", null)).StatusCode);
        var store = h.Factory.Services.GetRequiredService<IProviderCredentialStore>();
        Assert.Equal("good-groq-key-000001", await store.GetActiveCredentialAsync(ExternalProviders.Groq, default));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.PostAsJsonAsync($"/api/admin/credentials/groq/revoke/{v1.Id}", new { reason = "Leaked in a screenshot" })).StatusCode);
        // The environment key is not silently reused once keys are managed at runtime.
        Assert.Null(await store.GetActiveCredentialAsync(ExternalProviders.Groq, default));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Client.PostAsync($"/api/admin/credentials/groq/rollback/{v1.Id}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync($"/api/admin/credentials/groq/rollback/{v2.Id}", null)).StatusCode);
    }

    [Fact]
    public async Task OnlySuperAdminsWithARecentSecondFactorCanReplaceKeys()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("ai@nutripath.test", AdminRoles.AIManager);
        var manager = await h.SignInAsync("ai@nutripath.test");
        Assert.Equal(HttpStatusCode.OK, (await manager.Client.GetAsync("/api/admin/credentials")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.Client.PutAsJsonAsync("/api/admin/credentials/groq", new { apiKey = "good-groq-key-123456" })).StatusCode);

        await h.CreateUserAsync("super@nutripath.test", AdminRoles.SuperAdmin);
        var super = await h.SignInAsync("super@nutripath.test");
        using (var scope = h.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();
            foreach (var session in db.AdminSessions) session.MfaVerifiedAtUtc = DateTime.UtcNow.AddHours(-1);
            db.SaveChanges();
        }
        var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/admin/auth/refresh");
        refresh.Headers.Add(NutriPath.Api.Controllers.Admin.AdminAuthController.CsrfHeader, "1");
        var token = (await (await super.Client.SendAsync(refresh)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        super.Client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var stale = await super.Client.PutAsJsonAsync("/api/admin/credentials/groq", new { apiKey = "good-groq-key-123456" });
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.Contains("reauth_required", await stale.Content.ReadAsStringAsync());

        var reauth = await super.Client.PostAsJsonAsync("/api/admin/auth/reauth", new { password = AdminHarness.Password, code = AdminHarness.CurrentCode(super.Secret, 1) });
        Assert.Equal(HttpStatusCode.OK, reauth.StatusCode);
        super.Client.DefaultRequestHeaders.Authorization = new("Bearer", (await reauth.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await super.Client.PutAsJsonAsync("/api/admin/credentials/groq", new { apiKey = "good-groq-key-123456" })).StatusCode);
    }
}

public class AdminAiConfigurationTests
{
    [Fact]
    public async Task ConfigurationChangesApplyWithoutRestartAndCanBeRolledBack()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("super@nutripath.test", AdminRoles.SuperAdmin);
        var admin = await h.SignInAsync("super@nutripath.test");
        AiSettings Effective()
        {
            using var scope = h.Factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<AiSettings>>().Value;
        }
        Assert.Equal("Groq", Effective().Provider);

        var update = await admin.Client.PutAsJsonAsync("/api/admin/ai/configuration", new
        {
            configuration = new { provider = "Gemini", geminiModel = "gemini-3.5-flash-lite", geminiTemperature = 0.2, supportedLanguages = new[] { "en", "ta" }, defaultLanguage = "en" },
            expectedVersion = 0, note = "Pilot Gemini",
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("Gemini", Effective().Provider);
        Assert.Equal(["en", "ta"], Effective().Multilingual.SupportedLanguages);
        using (var scope = h.Factory.Services.CreateScope())
            Assert.Equal(0.2, scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<GeminiSettings>>().Value.Temperature);

        var conflict = await admin.Client.PutAsJsonAsync("/api/admin/ai/configuration", new { configuration = new { provider = "Groq" }, expectedVersion = 0 });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var invalid = await admin.Client.PutAsJsonAsync("/api/admin/ai/configuration", new { configuration = new { geminiTemperature = 3, supportedLanguages = new[] { "fr" } }, expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        await admin.Client.PutAsJsonAsync("/api/admin/ai/configuration", new { configuration = new { provider = "Groq" }, expectedVersion = 1 });
        Assert.Equal("Groq", Effective().Provider);
        var rollback = await admin.Client.PostAsJsonAsync("/api/admin/ai/configuration/rollback/1", new { expectedVersion = 2 });
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        Assert.Equal("Gemini", Effective().Provider);
        var history = await (await admin.Client.GetAsync("/api/admin/ai/configuration/history")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, history.GetArrayLength());
    }

    [Fact]
    public async Task DisablingAProviderStopsRequestsAndPrivacyControlsAreNotEditable()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("super@nutripath.test", AdminRoles.SuperAdmin);
        var admin = await h.SignInAsync("super@nutripath.test");
        var disable = await admin.Client.PutAsJsonAsync("/api/admin/ai/configuration", new
        {
            configuration = new { provider = "Gemini", groqEnabled = false, realUserDataApproved = true },
            expectedVersion = 0,
        });
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        using var scope = h.Factory.Services.CreateScope();
        Assert.False(scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<GeminiSettings>>().Value.RealUserDataApproved);
        var selector = scope.ServiceProvider.GetRequiredService<ILlmProviderSelector>();
        var consumer = await h.CreateUserAsync("student@uni.lk");
        await Assert.ThrowsAsync<LlmProviderNotPermittedException>(() => selector.SelectAsync(consumer.Id));
    }

    [Fact]
    public async Task UsageAnalyticsAndPerUserLimitsUseRecordedData()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("super@nutripath.test", AdminRoles.SuperAdmin);
        var consumer = await h.CreateUserAsync("student@uni.lk");
        var admin = await h.SignInAsync("super@nutripath.test");
        using (var scope = h.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();
            db.AiRequestMetrics.AddRange(
                new AiRequestMetric { UserId = consumer.Id, Provider = "Groq", Model = "openai/gpt-oss-120b", Language = "ta", TotalTokens = 1200, InputTokens = 1000, OutputTokens = 200, LatencyMs = 1500, Outcome = "Success", ValidationPassed = true },
                new AiRequestMetric { UserId = consumer.Id, Provider = "Gemini", Model = "gemini-3.5-flash-lite", Language = "si", TotalTokens = 0, LatencyMs = 8000, Outcome = "RateLimited" });
            db.SaveChanges();
        }
        var usage = await (await admin.Client.GetAsync("/api/admin/ai/usage")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, usage.GetProperty("totals").GetProperty("requests").GetInt32());
        Assert.Equal(1, usage.GetProperty("totals").GetProperty("rateLimited").GetInt32());
        Assert.Equal(1000, usage.GetProperty("totals").GetProperty("inputTokens").GetInt32());

        var set = await admin.Client.PutAsJsonAsync($"/api/admin/users/{consumer.Id}/usage-limits", new { tokenLimit = 1000, windowHours = 24, reason = "Abuse investigation" });
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
        using (var scope = h.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();
            db.AiUsageRecords.Add(new AiUsageRecord { UserId = consumer.Id, Tokens = 1100 });
            db.SaveChanges();
            var status = await scope.ServiceProvider.GetRequiredService<IAiUsageService>().GetStatusAsync(consumer.Id);
            Assert.True(status.Locked);
            Assert.Equal(1000, status.TokenLimit);
        }
        var overview = await admin.Client.GetAsync("/api/admin/dashboard/overview");
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        var trends = await admin.Client.GetAsync("/api/admin/dashboard/trends?range=7");
        Assert.Equal(HttpStatusCode.OK, trends.StatusCode);
        Assert.Equal(7, (await trends.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("series").GetArrayLength());
    }
}

public class AdminKnowledgeTests
{
    private static object Draft(string title, string content) => new
    {
        title, category = "macronutrients", organization = "World Health Organization",
        sourceUrl = "https://www.who.int/news-room/fact-sheets/detail/healthy-diet", content,
    };

    [Fact]
    public async Task DocumentsNeedAnIndependentReviewerAndOnlyApprovedOnesAreRetrieved()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("ai@nutripath.test", AdminRoles.AIManager);
        await h.CreateUserAsync("reviewer@nutripath.test", AdminRoles.NutritionReviewer);
        var author = await h.SignInAsync("ai@nutripath.test");
        var reviewer = await h.SignInAsync("reviewer@nutripath.test");

        var created = await author.Client.PostAsJsonAsync("/api/admin/knowledge/documents",
            Draft("Whole grain choices", "Whole grains such as red rice and kurakkan keep fibre and minerals that refining removes."));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var duplicate = await author.Client.PostAsJsonAsync("/api/admin/knowledge/documents",
            Draft("Copy", "Whole grains such as red rice and kurakkan keep fibre and minerals that refining removes."));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await author.Client.PostAsync($"/api/admin/knowledge/documents/{id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.Client.PostAsync($"/api/admin/knowledge/documents/{id}/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reviewer.Client.PostAsync($"/api/admin/knowledge/documents/{id}/approve", null)).StatusCode);

        using var scope = h.Factory.Services.CreateScope();
        var retrieval = scope.ServiceProvider.GetRequiredService<HybridKnowledgeRetrieval>();
        await retrieval.EnsureIndexBuiltAsync();
        await Task.Delay(TimeSpan.FromSeconds(11)); // keyword index refreshes every 10 s
        await retrieval.EnsureIndexBuiltAsync();
        var hits = await retrieval.SearchWithModeAsync("kurakkan whole grains fibre", "keyword", evaluationTopK: 3);
        Assert.Contains(hits, c => c.DocumentId == id);
    }

    [Fact]
    public async Task EditingAnApprovedDocumentCreatesANewVersionThatSupersedesIt()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("ai@nutripath.test", AdminRoles.AIManager);
        await h.CreateUserAsync("reviewer@nutripath.test", AdminRoles.NutritionReviewer);
        var author = await h.SignInAsync("ai@nutripath.test");
        var reviewer = await h.SignInAsync("reviewer@nutripath.test");
        var id = (await (await author.Client.PostAsJsonAsync("/api/admin/knowledge/documents",
            Draft("Hydration basics", "Water needs vary with climate and activity; drink regularly through the day."))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await author.Client.PostAsync($"/api/admin/knowledge/documents/{id}/submit", null);
        await reviewer.Client.PostAsync($"/api/admin/knowledge/documents/{id}/approve", null);

        var edit = await (await author.Client.PutAsJsonAsync($"/api/admin/knowledge/documents/{id}",
            Draft("Hydration basics", "Water needs vary with climate, activity and body size; drink regularly through the day."))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(edit.GetProperty("newVersion").GetBoolean());
        Assert.Equal(2, edit.GetProperty("version").GetInt32());
        var v2 = edit.GetProperty("id").GetGuid();

        using (var scope = h.Factory.Services.CreateScope())
            Assert.Equal(KnowledgeStatus.Approved, scope.ServiceProvider.GetRequiredService<NutriPathDbContext>().KnowledgeDocuments.Single(d => d.Id == id).Status);
        await author.Client.PostAsync($"/api/admin/knowledge/documents/{v2}/submit", null);
        await reviewer.Client.PostAsync($"/api/admin/knowledge/documents/{v2}/approve", null);
        using (var scope = h.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();
            var old = db.KnowledgeDocuments.Single(d => d.Id == id);
            Assert.Equal(KnowledgeStatus.Archived, old.Status);
            Assert.False(old.Approved);
            Assert.True(db.KnowledgeDocuments.Single(d => d.Id == v2).Approved);
        }
    }

    [Fact]
    public async Task RejectionArchivingAndUploadsFollowTheRules()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("ai@nutripath.test", AdminRoles.AIManager);
        await h.CreateUserAsync("reviewer@nutripath.test", AdminRoles.NutritionReviewer);
        var author = await h.SignInAsync("ai@nutripath.test");
        var reviewer = await h.SignInAsync("reviewer@nutripath.test");
        var id = (await (await author.Client.PostAsJsonAsync("/api/admin/knowledge/documents",
            Draft("Unverified claim", "Detox teas melt fat overnight and replace a balanced diet entirely."))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await author.Client.PostAsync($"/api/admin/knowledge/documents/{id}/submit", null);
        Assert.Equal(HttpStatusCode.BadRequest, (await reviewer.Client.PostAsJsonAsync($"/api/admin/knowledge/documents/{id}/reject", new { reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await reviewer.Client.PostAsJsonAsync($"/api/admin/knowledge/documents/{id}/reject", new { reason = "Not supported by the cited source" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await author.Client.PostAsJsonAsync($"/api/admin/knowledge/documents/{id}/archive", new { reason = "x" })).StatusCode);

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent("%PDF-1.7"u8.ToArray()), "file", "guide.pdf");
        Assert.Equal(HttpStatusCode.BadRequest, (await author.Client.PostAsync("/api/admin/knowledge/documents/import", form)).StatusCode);
        using var md = new MultipartFormDataContent();
        md.Add(new ByteArrayContent("# Fibre\nPulses and vegetables add fibre to Sri Lankan meals."u8.ToArray()), "file", "fibre.md");
        md.Add(new StringContent("Fibre in local meals"), "title");
        var parsed = await (await author.Client.PostAsync("/api/admin/knowledge/documents/import", md)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Fibre in local meals", parsed.GetProperty("title").GetString());
        Assert.Contains("Pulses", parsed.GetProperty("content").GetString());
    }

    [Fact]
    public async Task JobsFailCleanlyWithoutVectorStorageAndCanBeCancelled()
    {
        await using var h = new AdminHarness();
        using var scope = h.Factory.Services.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<BackgroundJobs>();
        var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();
        var id = await jobs.EnqueueAsync(BackgroundJobs.KnowledgeReindexAll, new { }, null, default);
        for (var i = 0; i < BackgroundJobs.MaxAttempts; i++) Assert.True(await jobs.RunNextAsync(default));
        var job = db.BackgroundJobs.AsNoTracking().Single(j => j.Id == id);
        Assert.Equal("Failed", job.Status);
        Assert.Equal(3, job.Attempts);
        Assert.Contains("embedding worker", job.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(await jobs.RunNextAsync(default));

        await h.CreateUserAsync("super@nutripath.test", AdminRoles.SuperAdmin);
        var admin = await h.SignInAsync("super@nutripath.test");
        var queued = await jobs.EnqueueAsync(BackgroundJobs.UsdaSync, new { query = "apple" }, null, default);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.PostAsync($"/api/admin/jobs/{queued}/cancel", null)).StatusCode);
        Assert.False(await jobs.RunNextAsync(default));
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync($"/api/admin/jobs/{id}/retry", null)).StatusCode);
    }
}

public class AdminFoodAndPlaygroundTests
{
    [Fact]
    public async Task CatalogueFoodsAreValidatedAndArchivedFoodsDisappearFromConsumerViews()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("reviewer@nutripath.test", AdminRoles.NutritionReviewer);
        var reviewer = await h.SignInAsync("reviewer@nutripath.test");
        object Food(decimal calories, decimal protein) => new
        {
            name = "Kurakkan roti", nameTamil = "குரக்கன் ரொட்டி", nameSinhala = "කුරක්කන් රොටි", category = "Breads",
            servingSizeGrams = 80, calories, proteinGrams = protein, carbsGrams = 40, fatGrams = 5, fiberGrams = 6,
            sugarGrams = 1, sodiumMilligrams = 150, allergens = (string?)null, isVerified = true,
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await reviewer.Client.PostAsJsonAsync("/api/admin/foods", Food(-5, 4))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await reviewer.Client.PostAsJsonAsync("/api/admin/foods", Food(2000, 4))).StatusCode);
        var created = await reviewer.Client.PostAsJsonAsync("/api/admin/foods", Food(220, 6));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using (var scope = h.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();
            var food = db.Foods.Single(f => f.Id == id);
            Assert.Null(food.Allergens); // unknown, not "allergen-free"
            Assert.Contains(db.Foods.Where(Models.Food.VisibleTo(Guid.NewGuid())), f => f.Id == id);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await reviewer.Client.PostAsync($"/api/admin/foods/{id}/archive", null)).StatusCode);
        using (var scope = h.Factory.Services.CreateScope())
            Assert.DoesNotContain(scope.ServiceProvider.GetRequiredService<NutriPathDbContext>().Foods.Where(Models.Food.VisibleTo(Guid.NewGuid())), f => f.Id == id);
    }

    [Fact]
    public async Task PlaygroundRunsThePipelineOnSyntheticDataOnly()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("ai@nutripath.test", AdminRoles.AIManager);
        var ai = await h.SignInAsync("ai@nutripath.test");
        var run = await ai.Client.PostAsJsonAsync("/api/admin/playground/run", new
        {
            question = "எனக்கு தினமும் எவ்வளவு புரதம் தேவை?", language = "auto", retrievalMode = "keyword", topK = 3,
            profile = "vegetarian-student", generate = false,
        });
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        var body = await run.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("synthetic").GetBoolean());
        var steps = body.GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal("ta", steps[0].GetProperty("resolved").GetString());
        Assert.Contains("protein", steps[2].GetProperty("mappedTerms").EnumerateArray().Select(t => t.GetString()));
        Assert.Contains("2173", steps[3].GetProperty("userPrompt").GetString());

        await h.CreateUserAsync("support@nutripath.test", AdminRoles.SupportAdmin);
        var support = await h.SignInAsync("support@nutripath.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await support.Client.PostAsJsonAsync("/api/admin/playground/run", new { question = "x", topK = 3 })).StatusCode);
    }
}
