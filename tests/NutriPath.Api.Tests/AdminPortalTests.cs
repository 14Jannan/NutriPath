using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Models.Admin;
using NutriPath.Api.Services;
using NutriPath.Api.Services.Admin;

namespace NutriPath.Api.Tests;

/// <summary>Spins up the real API and signs administrators in through the actual MFA flow.</summary>
internal sealed class AdminHarness : IAsyncDisposable
{
    public const string Password = "correct horse battery staple";
    public NutriPathApiFactory Factory { get; } = new HarnessFactory();

    // Every test request comes from one IP; the 5-per-minute auth limit is covered elsewhere.
    private sealed class HarnessFactory : NutriPathApiFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("RateLimits:AuthPermitPerMinute", "1000");
        }
    }

    public HttpClient Client() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    public T Service<T>() where T : notnull => Factory.Services.CreateScope().ServiceProvider.GetRequiredService<T>();

    public async Task<User> CreateUserAsync(string email, params string[] roles)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();
        var user = new User
        {
            Email = email, EmailVerified = true,
            PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(Password),
        };
        user.Profile = new UserProfile { UserId = user.Id, FullName = email.Split('@')[0] };
        db.Users.Add(user);
        foreach (var role in roles) db.UserRoles.Add(new UserRole { UserId = user.Id, Role = role });
        await db.SaveChangesAsync();
        return user;
    }

    public sealed record SignedIn(HttpClient Client, string AccessToken, string Secret, JsonElement Body);

    /// <summary>Password → enrol TOTP → session. Returns a client with the admin bearer token and refresh cookie.</summary>
    public async Task<SignedIn> SignInAsync(string email)
    {
        var client = Client();
        var login = await client.PostAsJsonAsync("/api/admin/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();
        var step = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("mfa-enroll", step.GetProperty("step").GetString());
        var secret = step.GetProperty("enrollmentSecret").GetString()!;
        var verify = await client.PostAsJsonAsync("/api/admin/auth/mfa/verify",
            new { ticket = step.GetProperty("ticket").GetString(), code = CurrentCode(secret) });
        verify.EnsureSuccessStatusCode();
        var body = await verify.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new SignedIn(client, token, secret, body);
    }

    public static string CurrentCode(string secret, int offset = 0) =>
        Totp.Code(secret, Totp.TimeStep(DateTimeOffset.UtcNow) + offset);

    public ValueTask DisposeAsync() => Factory.DisposeAsync();
}

public class AdminAuthenticationTests
{
    [Fact]
    public async Task FirstSignInEnrolsTotpAndReturnsRecoveryCodesOnce()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("super@nutripath.test", AdminRoles.SuperAdmin);
        var client = h.Client();
        var login = await (await client.PostAsJsonAsync("/api/admin/auth/login",
            new { email = "super@nutripath.test", password = AdminHarness.Password })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("mfa-enroll", login.GetProperty("step").GetString());
        Assert.StartsWith("otpauth://totp/", login.GetProperty("otpAuthUri").GetString());

        var wrong = await client.PostAsJsonAsync("/api/admin/auth/mfa/verify", new { ticket = login.GetProperty("ticket").GetString(), code = "000000" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        var ok = await client.PostAsJsonAsync("/api/admin/auth/mfa/verify", new
        {
            ticket = login.GetProperty("ticket").GetString(),
            code = AdminHarness.CurrentCode(login.GetProperty("enrollmentSecret").GetString()!),
        });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await ok.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10, body.GetProperty("recoveryCodes").GetArrayLength());
        Assert.Contains(body.GetProperty("profile").GetProperty("permissions").EnumerateArray(), p => p.GetString() == "ai.secrets.rotate");
        var cookie = Assert.Single(ok.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/admin/auth", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LaterSignInsNeedTotpAndRecoveryCodesWorkOnlyOnce()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("super@nutripath.test", AdminRoles.SuperAdmin);
        var first = await h.SignInAsync("super@nutripath.test");
        var recovery = first.Body.GetProperty("recoveryCodes")[0].GetString()!;

        async Task<HttpResponseMessage> Verify(object payload)
        {
            var client = h.Client();
            var login = await (await client.PostAsJsonAsync("/api/admin/auth/login",
                new { email = "super@nutripath.test", password = AdminHarness.Password })).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("mfa", login.GetProperty("step").GetString());
            Assert.False(login.TryGetProperty("enrollmentSecret", out var secret) && secret.ValueKind == JsonValueKind.String);
            var json = JsonSerializer.SerializeToElement(payload);
            var dict = json.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.GetString());
            dict["ticket"] = login.GetProperty("ticket").GetString();
            return await client.PostAsJsonAsync("/api/admin/auth/mfa/verify", dict);
        }

        Assert.Equal(HttpStatusCode.OK, (await Verify(new { recoveryCode = recovery.ToLowerInvariant() })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Verify(new { recoveryCode = recovery })).StatusCode);
        // The enrolment code was already used; the next step's code is accepted (one step of drift).
        Assert.Equal(HttpStatusCode.OK, (await Verify(new { code = AdminHarness.CurrentCode(first.Secret, 1) })).StatusCode);
    }

    [Fact]
    public async Task ConsumersCannotSignInAndConsumerTokensCannotReachAdminApis()
    {
        await using var h = new AdminHarness();
        var consumer = await h.CreateUserAsync("student@uni.lk");
        var client = h.Client();
        var login = await client.PostAsJsonAsync("/api/admin/auth/login", new { email = "student@uni.lk", password = AdminHarness.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        var consumerToken = h.Service<IJwtTokenService>().CreateAccessToken(consumer).token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", consumerToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ai/usage")).StatusCode);
    }

    [Fact]
    public async Task AdminTokensCannotUseTheConsumerApi()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("super@nutripath.test", AdminRoles.SuperAdmin);
        var admin = await h.SignInAsync("super@nutripath.test");
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.GetAsync("/api/admin/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.Client.GetAsync("/api/ai/usage")).StatusCode);
    }

    [Fact]
    public async Task FiveWrongPasswordsLockTheAccount()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("super@nutripath.test", AdminRoles.SuperAdmin);
        var client = h.Client();
        // The auth rate limit allows 5 requests per minute per IP, so count either outcome.
        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/admin/auth/login", new { email = "super@nutripath.test", password = "wrong password" });
        using var scope = h.Factory.Services.CreateScope();
        var user = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>().Users.Single(u => u.Email == "super@nutripath.test");
        Assert.True(user.LockoutEndUtc > DateTime.UtcNow);
        var service = scope.ServiceProvider.GetRequiredService<AdminAuthService>();
        var locked = await Assert.ThrowsAsync<AdminAuthException>(() => service.LoginAsync("super@nutripath.test", AdminHarness.Password));
        Assert.Equal(423, locked.Status);
    }

    [Fact]
    public async Task RefreshRotatesTheCookieAndNeedsTheCsrfHeader()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("super@nutripath.test", AdminRoles.SuperAdmin);
        var admin = await h.SignInAsync("super@nutripath.test");

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Client.PostAsync("/api/admin/auth/refresh", null)).StatusCode);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/auth/refresh");
        request.Headers.Add(NutriPath.Api.Controllers.Admin.AdminAuthController.CsrfHeader, "1");
        var refreshed = await admin.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);

        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/admin/auth/logout");
        logout.Headers.Add(NutriPath.Api.Controllers.Admin.AdminAuthController.CsrfHeader, "1");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.SendAsync(logout)).StatusCode);
        // The session is revoked: the still-unexpired access token stops working too.
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.Client.GetAsync("/api/admin/auth/me")).StatusCode);
    }
}

public class AdminAuthorizationTests
{
    [Fact]
    public async Task PermissionsAreEnforcedOnTheServer()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("support@nutripath.test", AdminRoles.SupportAdmin);
        var target = await h.CreateUserAsync("student@uni.lk");
        var support = await h.SignInAsync("support@nutripath.test");

        Assert.Equal(HttpStatusCode.OK, (await support.Client.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.Client.PostAsJsonAsync($"/api/admin/users/{target.Id}/suspend", new { reason = "testing access" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.Client.GetAsync("/api/admin/audit-logs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.Client.GetAsync("/api/admin/administrators")).StatusCode);
    }

    [Fact]
    public async Task SuspensionEndsSessionsImmediatelyAndBlocksSignIn()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("useradmin@nutripath.test", AdminRoles.UserAdmin);
        var consumer = await h.CreateUserAsync("student@uni.lk");
        var admin = await h.SignInAsync("useradmin@nutripath.test");
        var consumerClient = h.Client();
        consumerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            h.Service<IJwtTokenService>().CreateAccessToken(consumer).token);
        Assert.Equal(HttpStatusCode.OK, (await consumerClient.GetAsync("/api/ai/usage")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.PostAsJsonAsync($"/api/admin/users/{consumer.Id}/suspend", new { reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.PostAsJsonAsync($"/api/admin/users/{consumer.Id}/suspend", new { reason = "Repeated abuse reports" })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await consumerClient.GetAsync("/api/ai/usage")).StatusCode);
        var login = await h.Client().PostAsJsonAsync("/api/auth/login", new { email = "student@uni.lk", password = AdminHarness.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.PostAsJsonAsync($"/api/admin/users/{consumer.Id}/reactivate", new { reason = "Appeal accepted" })).StatusCode);
        var again = await h.Client().PostAsJsonAsync("/api/auth/login", new { email = "student@uni.lk", password = AdminHarness.Password });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.False((await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isAdmin").GetBoolean());
    }

    [Fact]
    public async Task UserAdminsCannotSuspendAdministrators()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("useradmin@nutripath.test", AdminRoles.UserAdmin);
        var super = await h.CreateUserAsync("super@nutripath.test", AdminRoles.SuperAdmin);
        var admin = await h.SignInAsync("useradmin@nutripath.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Client.PostAsJsonAsync($"/api/admin/users/{super.Id}/suspend", new { reason = "should not work" })).StatusCode);
    }

    [Fact]
    public async Task TheLastSuperAdminIsProtectedAndNobodyEditsTheirOwnRoles()
    {
        await using var h = new AdminHarness();
        var first = await h.CreateUserAsync("super@nutripath.test", AdminRoles.SuperAdmin);
        var second = await h.CreateUserAsync("super2@nutripath.test", AdminRoles.SuperAdmin);
        var admin = await h.SignInAsync("super@nutripath.test");

        var self = await admin.Client.PutAsJsonAsync($"/api/admin/administrators/{first.Id}/roles", new { roles = Array.Empty<string>(), reason = "self change" });
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        var demote = await admin.Client.PutAsJsonAsync($"/api/admin/administrators/{second.Id}/roles", new { roles = new[] { AdminRoles.AIManager }, reason = "Moving to AI team" });
        Assert.Equal(HttpStatusCode.NoContent, demote.StatusCode);
        // Now first is the last SuperAdmin; a suspension attempt by the second (who is now AIManager) is not even permitted.
        using var scope = h.Factory.Services.CreateScope();
        var control = scope.ServiceProvider.GetRequiredService<AccountControl>();
        var ex = await Assert.ThrowsAsync<AdminRuleException>(() => control.SuspendAsync(second.Id, first.Id, "attempted lockout", default));
        Assert.Contains("last active SuperAdmin", ex.Message);
    }

    [Fact]
    public async Task RoleChangesEndTheTargetsAdminSessions()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("super@nutripath.test", AdminRoles.SuperAdmin);
        var manager = await h.CreateUserAsync("ai@nutripath.test", AdminRoles.AIManager);
        var super = await h.SignInAsync("super@nutripath.test");
        var ai = await h.SignInAsync("ai@nutripath.test");
        Assert.Equal(HttpStatusCode.OK, (await ai.Client.GetAsync("/api/admin/auth/me")).StatusCode);

        var change = await super.Client.PutAsJsonAsync($"/api/admin/administrators/{manager.Id}/roles",
            new { roles = new[] { AdminRoles.AIManager, AdminRoles.NutritionReviewer }, reason = "Covering reviews" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ai.Client.GetAsync("/api/admin/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ConsumerAccountsCannotBePromotedToAdministrators()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("super@nutripath.test", AdminRoles.SuperAdmin);
        var consumer = await h.CreateUserAsync("student@uni.lk");
        var super = await h.SignInAsync("super@nutripath.test");
        var response = await super.Client.PutAsJsonAsync($"/api/admin/administrators/{consumer.Id}/roles",
            new { roles = new[] { AdminRoles.SupportAdmin }, reason = "promotion attempt" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task InvitationsCreateAdministratorsWhoThenEnrolMfa()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("super@nutripath.test", AdminRoles.SuperAdmin);
        var super = await h.SignInAsync("super@nutripath.test");
        var invite = await super.Client.PostAsJsonAsync("/api/admin/administrators/invitations",
            new { email = "reviewer@nutripath.test", roles = new[] { AdminRoles.NutritionReviewer } });
        Assert.Equal(HttpStatusCode.OK, invite.StatusCode);
        var link = (await invite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("link").GetString()!;
        var token = link[(link.IndexOf('#') + 1)..];

        var accept = await h.Client().PostAsJsonAsync("/api/admin/auth/invitations/accept",
            new { token, fullName = "Nutrition Reviewer", password = AdminHarness.Password });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        var reuse = await h.Client().PostAsJsonAsync("/api/admin/auth/invitations/accept",
            new { token, fullName = "Again", password = AdminHarness.Password });
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);

        var reviewer = await h.SignInAsync("reviewer@nutripath.test");
        var me = await (await reviewer.Client.GetAsync("/api/admin/auth/me")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["NutritionReviewer"], me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToArray());
    }

    [Fact]
    public async Task AuditRecordsSensitiveActionsWithoutSecrets()
    {
        await using var h = new AdminHarness();
        await h.CreateUserAsync("super@nutripath.test", AdminRoles.SuperAdmin);
        var consumer = await h.CreateUserAsync("student@uni.lk");
        var admin = await h.SignInAsync("super@nutripath.test");
        await admin.Client.PostAsJsonAsync($"/api/admin/users/{consumer.Id}/suspend", new { reason = "Spam account" });

        using var scope = h.Factory.Services.CreateScope();
        var logs = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>().AdminAuditLogs.ToList();
        Assert.Contains(logs, l => l.Action == "admin.mfa.enrolled");
        Assert.Contains(logs, l => l.Action == "admin.login" && l.Outcome == "Success");
        var suspend = Assert.Single(logs, l => l.Action == "user.suspend");
        Assert.Equal("Spam account", suspend.Reason);
        var all = JsonSerializer.Serialize(logs);
        Assert.DoesNotContain(admin.Secret, all);
        Assert.DoesNotContain(AdminHarness.Password, all);
        Assert.DoesNotContain(admin.AccessToken, all);
    }
}

public class AdminCryptoTests
{
    [Fact]
    public void TotpMatchesTheRfc6238TestVector()
    {
        // RFC 6238 appendix B, SHA-1, T = 59 s → 94287082 (8 digits); the last 6 digits are the 6-digit code.
        var secret = Totp.Base32Encode("12345678901234567890"u8.ToArray());
        Assert.Equal("287082", Totp.Code(secret, 59 / 30));
    }

    [Fact]
    public void TotpRejectsReplayAndWrongCodes()
    {
        var secret = Totp.NewSecret();
        var now = DateTimeOffset.UtcNow;
        var step = Totp.TimeStep(now);
        var code = Totp.Code(secret, step);
        Assert.Equal(step, Totp.Verify(secret, code, now, 0));
        Assert.Null(Totp.Verify(secret, code, now, step));
        Assert.Null(Totp.Verify(secret, "12345", now, 0));
        Assert.Null(Totp.Verify(secret, Totp.Code(secret, step + 5), now, 0));
    }

    [Fact]
    public void SecretsRoundTripWithFreshNoncesAndBindToTheirPurpose()
    {
        var protector = new AesGcmSecretProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Credentials:EncryptionKeys:1"] = Convert.ToBase64String(new byte[32]),
            ["Credentials:EncryptionKeys:2"] = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()),
            ["Credentials:ActiveKeyVersion"] = "2",
        }).Build());
        var a = protector.Protect("sk-test-123", "credential:Gemini:1");
        var b = protector.Protect("sk-test-123", "credential:Gemini:1");
        Assert.Equal(2, a.KeyVersion);
        Assert.NotEqual(a.Nonce, b.Nonce);
        Assert.NotEqual(a.Ciphertext, b.Ciphertext);
        Assert.Equal("sk-test-123", protector.Unprotect(a, "credential:Gemini:1"));
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => protector.Unprotect(a, "credential:Groq:1"));
        Assert.Equal("sk-test-123", protector.Unprotect(AesGcmSecretProtector.Unpack(AesGcmSecretProtector.Pack(a)), "credential:Gemini:1"));
    }

    [Fact]
    public void InvalidKeyConfigurationIsRejected()
    {
        IConfiguration Config(string key) => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Credentials:EncryptionKeys:1"] = key }).Build();
        Assert.Throws<InvalidOperationException>(() => new AesGcmSecretProtector(Config(Convert.ToBase64String(new byte[16]))));
        Assert.Throws<InvalidOperationException>(() => new AesGcmSecretProtector(Config("not base64!")));
        Assert.False(new AesGcmSecretProtector(new ConfigurationBuilder().Build()).Configured);
    }
}
