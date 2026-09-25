using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NutriPath.Api.Data;

namespace NutriPath.Api.Tests;

/// <summary>
/// Boots the REAL app (Program.cs, DI, middleware, routing, auth) in memory,
/// swapping only what a test run can't have: Postgres becomes an in-memory
/// database and the JWT key is a throwaway test value. This keeps the tests
/// runnable anywhere, including CI, with no database or user secrets.
/// </summary>
public class NutriPathApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not "Development": user secrets and appsettings.Development.json
        // stay out of tests, so they behave the same on every machine.
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Key", "test-only-signing-key-that-is-long-enough-for-hmac-sha256");
        builder.UseSetting("Jwt:Issuer", "NutriPath.Api");
        builder.UseSetting("Jwt:Audience", "NutriPath.Client");
        // A fixed, test-only 32-byte key (base64 of "nutripath-test-encryption-key-01").
        builder.UseSetting("Encryption:MessageKey", "bnV0cmlwYXRoLXRlc3QtZW5jcnlwdGlvbi1rZXktMDE=");

        builder.ConfigureServices(services =>
        {
            // Remove the Npgsql registration entirely (options AND the
            // configuration callback), or EF would see two providers.
            services.RemoveAll<DbContextOptions<NutriPathDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<NutriPathDbContext>>();
            services.AddDbContext<NutriPathDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }
}
