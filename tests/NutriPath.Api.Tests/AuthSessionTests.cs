using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NutriPath.Api.Data;
using NutriPath.Api.DTOs;
using NutriPath.Api.Models;
using NutriPath.Api.Services;

namespace NutriPath.Api.Tests;

public class AuthSessionTests
{
    private const string Origin = "https://app.example.test";
    private const string Password = "test-only-password";

    [Fact]
    public async Task NativeLogin_RotatesAndRevokesRefreshToken()
    {
        await using var factory = new NutriPathApiFactory();
        var email = SeedUser(factory);
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();
        Assert.False(login.Headers.Contains("Set-Cookie"));
        var session = (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/profile/me")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IOptions<JwtSettings>>().Value;
        var expiredIssuer = new JwtTokenService(Options.Create(new JwtSettings
        {
            Key = jwt.Key, Issuer = jwt.Issuer, Audience = jwt.Audience, AccessTokenMinutes = -5,
        }));
        var (expired, _) = expiredIssuer.CreateAccessToken(new User { Id = session.UserId, Email = email });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expired);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/profile/me")).StatusCode);

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { session.RefreshToken });
        refresh.EnsureSuccessStatusCode();
        var rotated = (await refresh.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rotated.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/profile/me")).StatusCode);
        Assert.NotEqual(session.RefreshToken, rotated.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/refresh", new { session.RefreshToken })).StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var body = new { rotated.RefreshToken };
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/logout", body)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/logout", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", body)).StatusCode);
    }

    [Fact]
    public async Task RefreshRejectsExpiredAndUnknownTokens()
    {
        await using var factory = new NutriPathApiFactory();
        SeedUser(factory, "expired-token");
        using var client = factory.CreateClient();

        foreach (var token in new[] { "expired-token", "unknown-token" })
        {
            var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = token });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.DoesNotContain(token, await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task RefreshTokenCanOnlyBeRotatedOnce()
    {
        await using var factory = new NutriPathApiFactory();
        var email = SeedUser(factory);
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();
        var session = (await login.Content.ReadFromJsonAsync<AuthResponse>())!;

        var results = await Task.WhenAll(
            client.PostAsJsonAsync("/api/auth/refresh", new { session.RefreshToken }),
            client.PostAsJsonAsync("/api/auth/refresh", new { session.RefreshToken }));
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(false, "lax")]
    [InlineData(true, "none")]
    public async Task WebSession_RequiresTrustedOriginAndHeader_RotatesAndClearsCookie(bool crossSite, string sameSite)
    {
        await using var baseFactory = new NutriPathApiFactory();
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("WebAuth:AllowedOrigins:0", Origin);
            builder.UseSetting("WebAuth:CrossSiteCookie", crossSite.ToString());
        });
        var email = SeedUser(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://api.example.test"),
            HandleCookies = false,
        });
        var loginBody = new { email, password = Password };
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/auth/refresh");
        preflight.Headers.Add("Origin", Origin);
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type,x-nutripath-web");
        var preflightResponse = await client.SendAsync(preflight);
        Assert.Equal(HttpStatusCode.NoContent, preflightResponse.StatusCode);
        Assert.Equal(Origin, preflightResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("x-nutripath-web", preflightResponse.Headers.GetValues("Access-Control-Allow-Headers").Single());

        Assert.Equal(HttpStatusCode.Forbidden, (await WebPost(client, "login", loginBody, marker: false)).StatusCode);

        var login = await WebPost(client, "login", loginBody);
        var cookie = await AssertWebSession(login, sameSite);
        Assert.Equal(Origin, login.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", login.Headers.GetValues("Access-Control-Allow-Credentials").Single());

        foreach (var action in new[] { "refresh", "logout" })
        {
            Assert.Equal(HttpStatusCode.Forbidden,
                (await WebPost(client, action, new { }, cookie, origin: "https://untrusted.example.test")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await WebPost(client, action, new { }, cookie, marker: false)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await WebPost(client, action, new { }, cookie, origin: null)).StatusCode);
        }

        var rotated = await AssertWebSession(await WebPost(client, "refresh", new { }, cookie), sameSite);
        Assert.NotEqual(cookie, rotated);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WebPost(client, "refresh", new { }, cookie)).StatusCode);
        var logout = await WebPost(client, "logout", new { }, rotated);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var deletedCookie = logout.Headers.GetValues("Set-Cookie").Single().ToLowerInvariant();
        Assert.StartsWith("nutripath_refresh=;", deletedCookie);
        Assert.Contains("expires=", deletedCookie);
        Assert.Contains("path=/api/auth", deletedCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WebPost(client, "refresh", new { }, rotated)).StatusCode);
    }

    private static string SeedUser(WebApplicationFactory<Program> factory, string? expiredToken = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();
        var user = TestDb.AddUser(db);
        user.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(Password);
        if (expiredToken is not null)
        {
            user.RefreshTokenHash = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().HashRefreshToken(expiredToken);
            user.RefreshTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        }
        db.SaveChanges();
        return user.Email;
    }

    private static async Task<HttpResponseMessage> WebPost(HttpClient client, string action, object body,
        string? cookie = null, string? origin = Origin, bool marker = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/auth/{action}") { Content = JsonContent.Create(body) };
        if (origin is not null) request.Headers.Add("Origin", origin);
        if (marker) request.Headers.Add("X-NutriPath-Web", "1");
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }

    private static async Task<string> AssertWebSession(HttpResponseMessage response, string sameSite)
    {
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.TryGetProperty("refreshToken", out _));
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("accessToken").GetString()));
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.StartsWith("nutripath_refresh=", cookie);
        var flags = cookie.ToLowerInvariant();
        Assert.Contains("; secure", flags);
        Assert.Contains("; httponly", flags);
        Assert.Contains($"samesite={sameSite}", flags);
        Assert.Contains("path=/api/auth", flags);
        return cookie.Split(';')[0];
    }
}
