using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Services;

namespace NutriPath.Api.Tests;

public class AiProviderFailureTests
{
    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(0)]
    public async Task ProviderFailuresReturnSanitizedRetryableResponse(int upstreamStatus)
    {
        await using var factory = new NutriPathApiFactory();
        await using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddScoped<IAiContextBuilder, EmptyContext>();
            services.AddHttpClient<IGroqClient, GroqClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new FailingProvider(upstreamStatus));
        }));
        using var client = configured.CreateClient();
        using var scope = configured.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();
        var user = TestDb.AddUser(db);
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().CreateAccessToken(user).token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await client.PostAsJsonAsync("/api/ai/chat", new { question = "What is my calorie goal?" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("""{"message":"The assistant is temporarily unavailable. Please try again."}""",
            await response.Content.ReadAsStringAsync());
        Assert.Empty(db.AiMessages);
        Assert.Empty(db.AiUsageRecords);
    }

    private sealed class EmptyContext : IAiContextBuilder
    {
        public Task<string> BuildContextAsync(Guid userId, string userQuestion, ClientClock clock) =>
            Task.FromResult("{}");
    }

    private sealed class FailingProvider(int upstreamStatus) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            upstreamStatus == 0
                ? Task.FromException<HttpResponseMessage>(new TaskCanceledException("private upstream timeout details"))
                : Task.FromResult(new HttpResponseMessage((HttpStatusCode)upstreamStatus)
                {
                    Content = new StringContent("private upstream response details"),
                });
    }
}
