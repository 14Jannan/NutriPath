using System.Net;
using System.Net.Http.Json;

namespace NutriPath.Api.Tests;

// Real HTTP requests through the full middleware pipeline, so these catch
// wiring mistakes (routes, [Authorize], rate limiting) that unit tests can't.
public class ApiEndpointTests : IClassFixture<NutriPathApiFactory>
{
    private readonly NutriPathApiFactory _factory;

    public ApiEndpointTests(NutriPathApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _factory.CreateClient().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/profile/me")]
    [InlineData("/api/meals/daily")]
    [InlineData("/api/nutrition/weekly-score")]
    [InlineData("/api/ai/history")]
    [InlineData("/api/sync/status")]
    public async Task ProtectedEndpoints_WithoutToken_Return401(string url)
    {
        var response = await _factory.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_IsRateLimitedAfterFiveAttempts()
    {
        var client = _factory.CreateClient();
        var body = new { email = "nobody@example.com", password = "wrong-password" };

        for (var i = 0; i < 5; i++)
        {
            var attempt = await client.PostAsJsonAsync("/api/auth/login", body);
            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        }

        var blocked = await client.PostAsJsonAsync("/api/auth/login", body);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }
}
