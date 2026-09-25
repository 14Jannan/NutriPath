using System.Security.Cryptography;
using NutriPath.Api.Models;
using NutriPath.Api.Services;

namespace NutriPath.Api.Tests;

public class MessageEncryptionTests
{
    private static readonly AesGcmMessageProtector Protector = AiServiceTests.Protector;

    [Fact]
    public void Protect_RoundTrips_IncludingNonEnglishText()
    {
        const string text = "What can I eat tonight? කොත්තු / கொத்து 🍛";

        var stored = Protector.Protect(text);

        Assert.StartsWith(AesGcmMessageProtector.Prefix, stored);
        Assert.DoesNotContain("tonight", stored);
        Assert.Equal(text, Protector.Unprotect(stored));
    }

    [Fact]
    public void Protect_SameMessageTwice_StoresDifferentText()
    {
        // A random nonce per message: identical messages can't be spotted
        // (or matched against a guess) by comparing stored values.
        Assert.NotEqual(Protector.Protect("hello"), Protector.Protect("hello"));
    }

    [Fact]
    public void Unprotect_DetectsTampering()
    {
        var stored = Protector.Protect("my private message");
        var bytes = Convert.FromBase64String(stored[AesGcmMessageProtector.Prefix.Length..]);
        bytes[^1] ^= 0xFF; // flip bits in the authentication tag

        var tampered = AesGcmMessageProtector.Prefix + Convert.ToBase64String(bytes);

        Assert.ThrowsAny<CryptographicException>(() => Protector.Unprotect(tampered));
    }

    [Fact]
    public void Unprotect_WithADifferentKey_Fails()
    {
        var otherKey = new AesGcmMessageProtector(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

        Assert.ThrowsAny<CryptographicException>(() => otherKey.Unprotect(Protector.Protect("secret")));
    }

    [Fact]
    public void Unprotect_ReturnsOldPlaintextRowsUnchanged()
    {
        Assert.Equal("saved before encryption", Protector.Unprotect("saved before encryption"));
    }

    [Theory]
    [InlineData("not base64 at all!")]
    [InlineData("c2hvcnQ=")] // valid base64, but only 5 bytes
    public void Constructor_RejectsBadKeys(string key)
    {
        Assert.Throws<InvalidOperationException>(() => new AesGcmMessageProtector(key));
    }

    [Fact]
    public async Task Chat_StoresOnlyEncryptedText_ButShowsTheOwnerPlaintext()
    {
        using var db = TestDb.Create();
        var user = TestDb.AddUser(db);
        var service = new AiService(new StubContext(), new StubRetrieval(), new StubGroq(), db, Protector);
        var clock = new ClientClock(new DateOnly(2026, 9, 25), null);

        var reply = await service.ChatAsync(user.Id, "I have diabetes, what can I eat?", clock);

        // What a person reading the database would see:
        Assert.All(db.AiMessages, m =>
        {
            Assert.StartsWith(AesGcmMessageProtector.Prefix, m.Content);
            Assert.DoesNotContain("diabetes", m.Content);
            if (m.RetrievedContextJson != null) Assert.StartsWith(AesGcmMessageProtector.Prefix, m.RetrievedContextJson);
        });

        // What the owner sees in the app:
        var messages = await service.GetConversationMessagesAsync(user.Id, reply.ConversationId);
        Assert.Equal("I have diabetes, what can I eat?", messages[0].Content);
        Assert.Equal("Try dhal and red rice.", messages[1].Content);
        Assert.Equal("I have diabetes, what can I eat?", (await service.ListConversationsAsync(user.Id))[0].Title);
        Assert.Single(await service.SearchAsync(user.Id, "DIABETES"));
    }

    [Fact]
    public async Task Migrator_EncryptsOldPlaintextRows_AndIsSafeToRunAgain()
    {
        using var db = TestDb.Create();
        var user = TestDb.AddUser(db);
        var conversation = new AiConversation { UserId = user.Id };
        db.AiConversations.Add(conversation);
        db.AiMessages.Add(new AiMessage { ConversationId = conversation.Id, Role = AiMessageRole.User, Content = "old plain message" });
        db.AiMessages.Add(new AiMessage
        {
            ConversationId = conversation.Id,
            Role = AiMessageRole.Assistant,
            Content = "old plain answer",
            RetrievedContextJson = "{\"allergies\":[\"Peanuts\"]}",
        });
        await db.SaveChangesAsync();

        Assert.Equal(2, await MessageEncryptionMigrator.EncryptExistingAsync(db, Protector));
        Assert.Equal(0, await MessageEncryptionMigrator.EncryptExistingAsync(db, Protector)); // nothing left to do

        Assert.All(db.AiMessages, m => Assert.StartsWith(AesGcmMessageProtector.Prefix, m.Content));
        var answer = db.AiMessages.Single(m => m.Role == AiMessageRole.Assistant);
        Assert.Equal("old plain answer", Protector.Unprotect(answer.Content));
        Assert.Contains("Peanuts", Protector.Unprotect(answer.RetrievedContextJson!));
    }

    private sealed class StubContext : IAiContextBuilder
    {
        public Task<string> BuildContextAsync(Guid userId, string userQuestion, ClientClock clock) => Task.FromResult("{}");
    }

    private sealed class StubRetrieval : IKnowledgeRetrievalService
    {
        public Task EnsureIndexBuiltAsync() => Task.CompletedTask;
        public List<KnowledgeChunk> Search(string query, int topK) => new();
    }

    private sealed class StubGroq : IGroqClient
    {
        public Task<string> AskAsync(string systemPrompt, string userMessage, IReadOnlyList<GroqMessage>? history = null) =>
            Task.FromResult("Try dhal and red rice.");
    }
}
