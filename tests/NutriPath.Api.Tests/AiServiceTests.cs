using Microsoft.Extensions.Options;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Services;

namespace NutriPath.Api.Tests;

public class AiServiceTests
{
    private sealed class FakeContext : IAiContextBuilder
    {
        public ClientClock? LastClock { get; private set; }

        public Task<string> BuildContextAsync(Guid userId, string userQuestion, ClientClock clock)
        {
            LastClock = clock;
            return Task.FromResult("{}");
        }
    }

    private sealed class FakeRetrieval : IKnowledgeRetrievalService
    {
        public Task EnsureIndexBuiltAsync() => Task.CompletedTask;
        public List<KnowledgeChunk> Search(string query, int topK) => new();
    }

    private sealed class FakeGroq : IGroqClient
    {
        public List<IReadOnlyList<GroqMessage>> Histories { get; } = new();
        private int _replies;

        public Task<GroqReply> AskAsync(string systemPrompt, string userMessage, IReadOnlyList<GroqMessage>? history = null)
        {
            Histories.Add(history ?? Array.Empty<GroqMessage>());
            return Task.FromResult(new GroqReply($"answer {++_replies}", 1000));
        }
    }

    // Test-only key: base64 of "nutripath-test-encryption-key-01" (32 bytes).
    internal static readonly AesGcmMessageProtector Protector = new("bnV0cmlwYXRoLXRlc3QtZW5jcnlwdGlvbi1rZXktMDE=");

    private static readonly ClientClock Clock =
        new(new DateOnly(2026, 9, 25), new DateTimeOffset(2026, 9, 25, 19, 30, 0, TimeSpan.FromHours(5.5)));

    private static (AiService Service, FakeGroq Groq, FakeContext Context, NutriPathDbContext Db) Create()
    {
        var db = TestDb.Create();
        var groq = new FakeGroq();
        var context = new FakeContext();
        return (new AiService(context, new FakeRetrieval(), groq, db, Protector), groq, context, db);
    }

    [Fact]
    public async Task Chat_SendsEarlierMessagesAsMemory_AndTheUsersLocalClock()
    {
        var (service, groq, context, db) = Create();
        var user = TestDb.AddUser(db);

        var first = await service.ChatAsync(user.Id, "What should I eat tonight?", Clock);
        await service.ChatAsync(user.Id, "What about lunch tomorrow?", Clock, first.ConversationId);

        Assert.Empty(groq.Histories[0]); // nothing before the first question
        var memory = groq.Histories[1];
        Assert.Equal(new[] { "user", "assistant" }, memory.Select(m => m.Role));
        Assert.Equal("What should I eat tonight?", memory[0].Content);
        Assert.Equal("answer 1", memory[1].Content);

        Assert.Equal(Clock, context.LastClock);
        Assert.Equal(4, db.AiMessages.Count());
    }

    [Fact]
    public async Task Chat_ContinuesTheChosenConversation_NotJustTheLatest()
    {
        var (service, groq, _, db) = Create();
        var user = TestDb.AddUser(db);
        var old = await service.ChatAsync(user.Id, "Old topic: protein", Clock);
        await service.StartNewConversationAsync(user.Id);
        await service.ChatAsync(user.Id, "New topic: fibre", Clock);

        var reply = await service.ChatAsync(user.Id, "Tell me more", Clock, old.ConversationId);

        Assert.Equal(old.ConversationId, reply.ConversationId);
        Assert.Contains(groq.Histories.Last(), m => m.Content == "Old topic: protein");
        Assert.DoesNotContain(groq.Histories.Last(), m => m.Content == "New topic: fibre");
    }

    [Fact]
    public async Task Chat_RejectsAnotherUsersConversation()
    {
        var (service, _, _, db) = Create();
        var owner = TestDb.AddUser(db);
        var other = TestDb.AddUser(db);
        var chat = await service.ChatAsync(owner.Id, "Hello", Clock);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ChatAsync(other.Id, "Hi", Clock, chat.ConversationId));
    }

    [Fact]
    public async Task ListConversations_TitlesByFirstQuestion_NewestFirst_SkippingEmptyOnes()
    {
        var (service, _, _, db) = Create();
        var user = TestDb.AddUser(db);
        await service.ChatAsync(user.Id, "First chat about breakfast", Clock);
        await service.StartNewConversationAsync(user.Id);
        await service.ChatAsync(user.Id, "Second chat about dinner", Clock);
        await service.StartNewConversationAsync(user.Id); // empty: not listed

        var list = await service.ListConversationsAsync(user.Id);

        Assert.Equal(new[] { "Second chat about dinner", "First chat about breakfast" }, list.Select(c => c.Title));
        Assert.All(list, c => Assert.Equal(2, c.MessageCount));
    }

    [Fact]
    public async Task Search_FindsOwnMessagesCaseInsensitively_WithTitleAndSnippet()
    {
        var (service, _, _, db) = Create();
        var user = TestDb.AddUser(db);
        var other = TestDb.AddUser(db);
        await service.ChatAsync(user.Id, "Is DHAL good for protein?", Clock);
        await service.ChatAsync(other.Id, "dhal recipe please", Clock);

        var results = await service.SearchAsync(user.Id, "dhal");

        var hit = Assert.Single(results); // the other user's message isn't included
        Assert.Equal("Is DHAL good for protein?", hit.ConversationTitle);
        Assert.Contains("DHAL", hit.Snippet);
        Assert.Equal("User", hit.Role);
    }

    [Fact]
    public async Task DeleteConversation_RemovesItsMessages_OnlyForTheOwner()
    {
        var (service, _, _, db) = Create();
        var owner = TestDb.AddUser(db);
        var other = TestDb.AddUser(db);
        var chat = await service.ChatAsync(owner.Id, "Hello", Clock);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeleteConversationAsync(other.Id, chat.ConversationId));
        await service.DeleteConversationAsync(owner.Id, chat.ConversationId);

        Assert.Empty(await service.ListConversationsAsync(owner.Id));
    }

    private static AiUsageService Usage(NutriPathDbContext db, int limit = 3000, int hours = 5) =>
        new(db, Options.Create(new AiUsageSettings { TokenLimit = limit, WindowHours = hours }));

    [Fact]
    public async Task Usage_CountsEachReplysTokens_EvenAfterTheChatIsDeleted()
    {
        var (service, _, _, db) = Create();
        var user = TestDb.AddUser(db);
        var chat = await service.ChatAsync(user.Id, "Hello", Clock); // the fake costs 1000 tokens
        await service.DeleteConversationAsync(user.Id, chat.ConversationId);

        var status = await Usage(db).GetStatusAsync(user.Id);

        Assert.Equal(1000, status.TokensUsed);
        Assert.False(status.Locked);
        Assert.Null(status.ResetsAtUtc);
    }

    [Fact]
    public async Task Usage_LocksAtTheLimit_UntilEnoughOldTokensLeaveTheWindow()
    {
        var db = TestDb.Create();
        var user = TestDb.AddUser(db);
        var other = TestDb.AddUser(db);
        var now = DateTime.UtcNow;
        db.AiUsageRecords.AddRange(
            new AiUsageRecord { UserId = user.Id, Tokens = 5000, CreatedAtUtc = now.AddHours(-6) }, // outside the window
            new AiUsageRecord { UserId = user.Id, Tokens = 1500, CreatedAtUtc = now.AddHours(-4) },
            new AiUsageRecord { UserId = user.Id, Tokens = 1000, CreatedAtUtc = now.AddHours(-2) },
            new AiUsageRecord { UserId = user.Id, Tokens = 1000, CreatedAtUtc = now.AddHours(-1) },
            new AiUsageRecord { UserId = other.Id, Tokens = 9000, CreatedAtUtc = now });
        db.SaveChanges();

        var status = await Usage(db).GetStatusAsync(user.Id);

        Assert.Equal(3500, status.TokensUsed);
        Assert.True(status.Locked);
        // Dropping the oldest record (1500) leaves 2000, back under 3000.
        Assert.Equal(now.AddHours(1), status.ResetsAtUtc);
    }

    [Fact]
    public void ClientClock_AcceptsRealLocalTimes_AndRejectsWrongClocks()
    {
        var sriLankaNow = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5));
        Assert.True(ClientDate.TryResolveClock(null, sriLankaNow, out var clock));
        Assert.Equal(DateOnly.FromDateTime(sriLankaNow.DateTime), clock.Today);

        Assert.False(ClientDate.TryResolveClock(null, DateTimeOffset.UtcNow.AddDays(3), out _));
    }
}
