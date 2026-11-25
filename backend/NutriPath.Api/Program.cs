using System.Text;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using NutriPath.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Services;

var knowledgeCommand = args.FirstOrDefault() == "knowledge";
var adminCommand = args.FirstOrDefault() == "admin";
var builder = WebApplication.CreateBuilder(knowledgeCommand || adminCommand ? [] : args);

// Render (and most cloud hosts) assign the port at runtime via PORT; Kestrel
// won't pick it up on its own. Locally PORT is unset, so launchSettings.json
// keeps working for `dotnet run`.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));
builder.Services.Configure<GroqSettings>(builder.Configuration.GetSection("Groq"));
builder.Services.Configure<AiUsageSettings>(builder.Configuration.GetSection("AiUsage"));
builder.Services.AddScoped<IFoodSearchService, FoodSearchService>();
builder.Services.AddScoped<IFoodLookupService, FoodLookupService>();
// Remembers which terms were recently looked up in USDA (see FoodLookupService).
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IMealService, MealService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IProfileInsightService, ProfileInsightService>();
builder.Services.AddScoped<INutritionService, NutritionService>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "NutriPath API",
        Version = "v1",
        Description = "Nutrition tracking with deterministic scoring and a grounded AI assistant. " +
                      "General wellness guidance only, not medical advice.",
    });

    // Shows the controllers' /// comments in Swagger UI.
    var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
    if (File.Exists(xmlPath)) options.IncludeXmlComments(xmlPath);

    // Adds the "Authorize" button in Swagger's UI, so we can paste a
    // bearer token in and test protected endpoints from the browser.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Paste a JWT access token here (no 'Bearer ' prefix needed).",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
    });
    options.AddSecurityRequirement(document =>
    {
        var requirement = new OpenApiSecurityRequirement();
        requirement.Add(new OpenApiSecuritySchemeReference("Bearer", document, null), new List<string>());
        return requirement;
    });
});

var allowedOrigins = builder.Configuration.GetSection("WebAuth:AllowedOrigins").Get<string[]>() ?? [];
if (allowedOrigins.Length == 0)
{
    var legacyOrigin = builder.Configuration["AllowedOrigin"];
    allowedOrigins = !string.IsNullOrWhiteSpace(legacyOrigin) ? [legacyOrigin]
        : builder.Environment.IsDevelopment()
            ? ["http://localhost:8081", "http://localhost:19006"]
            : [];
}
if (allowedOrigins.Any(origin =>
    !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
    || origin != uri.GetLeftPart(UriPartial.Authority)
    || origin.Contains('*')
    || (uri.Scheme != "https" && !(builder.Environment.IsDevelopment() && uri.Scheme == "http"))))
    throw new InvalidOperationException("Configure WebAuth allowed origins as exact frontend origins without a trailing slash.");

var crossSiteCookie = builder.Configuration.GetValue<bool>("WebAuth:CrossSiteCookie");
builder.Services.AddSingleton(new WebAuthSettings(
    allowedOrigins, !builder.Environment.IsDevelopment() || crossSiteCookie, crossSiteCookie));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader().AllowCredentials()));

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
    foreach (var address in builder.Configuration.GetSection("Https:KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(IPAddress.Parse(address));
});

builder.Services.AddRateLimiter(options =>
{
    // Brute-force protection for login, register and OTP/reset codes:
    // 5 attempts per minute PER CLIENT IP. A single shared window would
    // let one attacker lock every user out of logging in.
    options.AddPolicy(RateLimitPolicies.Auth, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimits:AuthPermitPerMinute", 5),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0, // reject immediately, no queueing
            }));

    // The goals screen asks for a fresh AI insight as values change. The
    // app debounces and caches, but this caps each user so a runaway
    // client can't burn through the Groq quota: 10 per minute per user.
    options.AddPolicy(RateLimitPolicies.AiInsight, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddSingleton<IHealthService, HealthService>();
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmailService, EmailService>();

builder.Services.AddScoped<INutritionCalculationService, NutritionCalculationService>();
builder.Services.AddScoped<IWeeklyScoreService, WeeklyScoreService>();
builder.Services.AddScoped<IAiContextBuilder, AiContextBuilder>();
// Explicit factory: AiService also has a single-provider constructor for tests and tools.
builder.Services.AddScoped<IAiService>(sp => new AiService(
    sp.GetRequiredService<IAiContextBuilder>(),
    sp.GetRequiredService<IKnowledgeRetrievalService>(),
    sp.GetRequiredService<NutriPath.Api.Services.Llm.ILlmProviderSelector>(),
    sp.GetRequiredService<NutriPath.Api.Services.Multilingual.ILanguageResolver>(),
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsSnapshot<AiSettings>>(),
    sp.GetRequiredService<NutriPathDbContext>(),
    sp.GetRequiredService<IMessageProtector>(),
    sp.GetRequiredService<NutriPath.Api.Services.Admin.RuntimeSettingsStore>(),
    sp.GetRequiredService<NutriPath.Api.Services.Admin.PlatformSettingsStore>()));
// Snapshot options: admin runtime settings apply on the next request without a restart.
builder.Services.AddScoped<IAiUsageService>(sp => new AiUsageService(sp.GetRequiredService<NutriPathDbContext>(),
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsSnapshot<AiUsageSettings>>()));

// Chat messages are encrypted at rest with this key. It lives in user
// secrets (dev) or the Encryption__MessageKey environment variable (prod),
// never in the repo. Losing it makes stored chats unreadable, so it's
// required up front rather than failing on the first message.
var messageKey = builder.Configuration["Encryption:MessageKey"];
if (string.IsNullOrWhiteSpace(messageKey))
    throw new InvalidOperationException(
        "Encryption:MessageKey is not configured. Generate one with: " +
        "dotnet user-secrets set \"Encryption:MessageKey\" \"<32 random bytes, base64>\"");
builder.Services.AddSingleton<IMessageProtector>(new AesGcmMessageProtector(messageKey));

// Singleton: the TF-IDF index is built once and shared by every request.
// It depends on the Scoped DbContext only via a short-lived scope.
builder.Services.AddSingleton<KnowledgeRetrievalService>();
builder.Services.AddScoped<HybridKnowledgeRetrieval>();
builder.Services.AddScoped<IKnowledgeRetrievalService>(sp => sp.GetRequiredService<HybridKnowledgeRetrieval>());
builder.Services.AddScoped<KnowledgeCli>();
builder.Services.AddHttpClient<IEmbeddingService, LocalEmbeddingService>(client =>
    client.Timeout = TimeSpan.FromSeconds(8))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false,
    });

builder.Services.AddHttpClient<IUsdaFoodSyncService, UsdaFoodSyncService>();

// Imports everyday foods on startup when the catalog is nearly empty.
builder.Services.AddHostedService<StarterFoodCatalogSeeder>();
builder.Services.AddHttpClient<IGroqClient, GroqClient>((HttpClient http, IServiceProvider sp) => new GroqClient(http,
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsSnapshot<GroqSettings>>(),
        sp.GetRequiredService<ILogger<GroqClient>>(),
        sp.GetRequiredService<NutriPath.Api.Services.Admin.IProviderCredentialStore>()))
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));

// Multilingual assistant: provider selection (with the Gemini free-tier privacy guard),
// language resolution and one-request-at-a-time per user.
builder.Services.Configure<AiSettings>(builder.Configuration.GetSection("AI"));
builder.Services.Configure<GeminiSettings>(builder.Configuration.GetSection("Gemini"));
builder.Services.AddSingleton<ChatRequestGate>();
builder.Services.AddScoped<NutriPath.Api.Services.Multilingual.ILanguageResolver>(sp =>
    new NutriPath.Api.Services.Multilingual.LanguageResolver(sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsSnapshot<AiSettings>>()));
builder.Services.AddScoped<NutriPath.Api.Services.Llm.GroqLlmClient>();
// Per-attempt timeouts and retries live in GeminiClient; this only bounds the whole call.
builder.Services.AddHttpClient<NutriPath.Api.Services.Llm.GeminiClient, NutriPath.Api.Services.Llm.GeminiClient>((HttpClient http, IServiceProvider sp) => new NutriPath.Api.Services.Llm.GeminiClient(http,
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsSnapshot<GeminiSettings>>(),
        sp.GetRequiredService<ILogger<NutriPath.Api.Services.Llm.GeminiClient>>(),
        sp.GetRequiredService<NutriPath.Api.Services.Admin.IProviderCredentialStore>()))
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(150));
builder.Services.AddScoped<NutriPath.Api.Services.Llm.ILlmProviderSelector>(sp => new NutriPath.Api.Services.Llm.LlmProviderSelector(
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsSnapshot<AiSettings>>(),
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsSnapshot<GeminiSettings>>(),
    sp.GetRequiredService<NutriPathDbContext>(), sp));
builder.Services.AddScoped<NutriPath.Api.Services.Llm.GroqLlmClient>(sp => new NutriPath.Api.Services.Llm.GroqLlmClient(
    sp.GetRequiredService<IGroqClient>(), sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsSnapshot<GroqSettings>>()));

// Runtime-managed provider keys and AI settings (admin portal).
builder.Services.AddSingleton<NutriPath.Api.Services.Admin.IProviderCredentialStore, NutriPath.Api.Services.Admin.ProviderCredentialStore>();
builder.Services.AddHttpClient<NutriPath.Api.Services.Admin.IProviderConnectionTester, NutriPath.Api.Services.Admin.ProviderConnectionTester>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<NutriPath.Api.Services.Admin.ProviderCredentialService>();
builder.Services.AddSingleton<NutriPath.Api.Services.Admin.RuntimeSettingsStore>();
builder.Services.AddSingleton<NutriPath.Api.Services.Admin.AiRuntimeOverrides>();
builder.Services.AddSingleton<Microsoft.Extensions.Options.IPostConfigureOptions<AiSettings>>(sp => sp.GetRequiredService<NutriPath.Api.Services.Admin.AiRuntimeOverrides>());
builder.Services.AddSingleton<Microsoft.Extensions.Options.IPostConfigureOptions<GeminiSettings>>(sp => sp.GetRequiredService<NutriPath.Api.Services.Admin.AiRuntimeOverrides>());
builder.Services.AddSingleton<Microsoft.Extensions.Options.IPostConfigureOptions<GroqSettings>>(sp => sp.GetRequiredService<NutriPath.Api.Services.Admin.AiRuntimeOverrides>());
builder.Services.AddSingleton<Microsoft.Extensions.Options.IPostConfigureOptions<AiUsageSettings>>(sp => sp.GetRequiredService<NutriPath.Api.Services.Admin.AiRuntimeOverrides>());
builder.Services.AddScoped<NutriPath.Api.Services.Admin.RuntimeSettingsService>();
builder.Services.AddScoped<NutriPath.Api.Services.Admin.KnowledgeAdminService>();
builder.Services.AddScoped<NutriPath.Api.Services.Admin.BackgroundJobs>();
builder.Services.AddHostedService<NutriPath.Api.Services.Admin.BackgroundJobWorker>();
builder.Services.AddSingleton<NutriPath.Api.Services.Admin.SystemHealth>();
builder.Services.AddSingleton<NutriPath.Api.Services.Admin.PlatformSettingsStore>();
builder.Services.AddScoped<NutriPath.Api.Services.Admin.PlatformSettingsService>();
builder.Services.AddScoped<NutriPath.Api.Services.Admin.AdminNotifications>();
builder.Services.AddScoped<NutriPath.Api.Services.Admin.QualityService>();
builder.Services.AddScoped<NutriPath.Api.Services.Admin.TerminologyService>();
builder.Services.AddScoped<NutriPath.Api.Services.Admin.EvaluationService>();
builder.Services.AddScoped<NutriPath.Api.Services.Admin.DataRequestService>();

builder.Services.AddDbContext<NutriPathDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;
if (Encoding.UTF8.GetByteCount(jwtSettings.Key) < 32)
    throw new InvalidOperationException("Configure Jwt:Key with at least 32 bytes.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30), // small tolerance for clock drift, not the full 5-minute .NET default
        };
        // Suspended accounts and revoked sessions stop working on the next request.
        options.Events = NutriPath.Api.Services.Admin.SecurityState.Events(admin: false);
    })
    // Admin portal tokens: separate audience, issued only after MFA, never valid for the consumer API.
    .AddJwtBearer(NutriPath.Api.Services.Admin.AdminTokenService.AdminScheme, options =>
    {
        options.MapInboundClaims = false;
        var parameters = NutriPath.Api.Services.Admin.AdminTokenService.Parameters(
            jwtSettings, NutriPath.Api.Services.Admin.AdminTokenService.AdminAudience);
        parameters.NameClaimType = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub;
        parameters.RoleClaimType = System.Security.Claims.ClaimTypes.Role;
        options.TokenValidationParameters = parameters;
        options.Events = NutriPath.Api.Services.Admin.SecurityState.Events(admin: true);
    });

builder.Services.AddAuthorization(NutriPath.Api.Authorization.AdminPolicies.Add);

// Admin portal services.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<NutriPath.Api.Services.Admin.SecurityState>();
builder.Services.AddSingleton<NutriPath.Api.Services.Admin.AdminTokenService>();
builder.Services.AddSingleton<NutriPath.Api.Services.Admin.ISecretProtector, NutriPath.Api.Services.Admin.AesGcmSecretProtector>();
builder.Services.AddScoped<NutriPath.Api.Services.Admin.AdminAudit>();
builder.Services.AddScoped<NutriPath.Api.Services.Admin.AdminAuthService>();
builder.Services.AddScoped<NutriPath.Api.Services.Admin.AdminCli>();
builder.Services.AddScoped<NutriPath.Api.Services.Admin.AccountControl>();

var app = builder.Build();

if (knowledgeCommand)
{
    using var scope = app.Services.CreateScope();
    try
    {
        await scope.ServiceProvider.GetRequiredService<KnowledgeCli>().RunAsync(args[1..]);
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or
        System.Text.Json.JsonException or HttpRequestException or OperationCanceledException or
        DbUpdateException or Npgsql.NpgsqlException or FormatException)
    {
        Console.Error.WriteLine($"Knowledge command failed ({ex.GetType().Name}).");
        Environment.ExitCode = 1;
    }
    return;
}

if (adminCommand)
{
    using var scope = app.Services.CreateScope();
    try
    {
        await scope.ServiceProvider.GetRequiredService<NutriPath.Api.Services.Admin.AdminCli>().RunAsync(args[1..]);
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or DbUpdateException or
        Npgsql.NpgsqlException)
    {
        // The message is ours (no secrets); database errors print only their type.
        Console.Error.WriteLine(ex is ArgumentException or InvalidOperationException
            ? $"Admin command failed: {ex.Message}"
            : $"Admin command failed ({ex.GetType().Name}).");
        Environment.ExitCode = 1;
    }
    return;
}

// Seed the AI knowledge base on startup if it's empty. AppContext.BaseDirectory
// (where the compiled .dll lives, and where the .csproj copies the JSON) is
// used instead of a relative path, which would depend on the launch folder.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NutriPathDbContext>();
    if (!db.KnowledgeChunks.Any())
    {
        var jsonPath = Path.Combine(AppContext.BaseDirectory, "Data", "Seed", "knowledge-base.json");
        var json = File.ReadAllText(jsonPath);
        var chunks = System.Text.Json.JsonSerializer.Deserialize<List<KnowledgeChunk>>(
            json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (chunks != null)
        {
            db.KnowledgeChunks.AddRange(chunks);
            db.SaveChanges();
        }
    }

    // Curated Sri Lankan dishes USDA doesn't have. Cheap to re-run: it only
    // writes foods that are new or whose values changed in the file.
    var sriLankanFoods = SriLankanFoodSeeder.SeedAsync(
        db, Path.Combine(AppContext.BaseDirectory, "Data", "Seed", "sri-lankan-foods.json")).GetAwaiter().GetResult();
    if (sriLankanFoods > 0)
        app.Logger.LogInformation("Added or updated {Count} Sri Lankan dishes.", sriLankanFoods);

    // Encrypt any chat messages saved before encryption was added.
    var protector = scope.ServiceProvider.GetRequiredService<IMessageProtector>();
    var encrypted = MessageEncryptionMigrator.EncryptExistingAsync(db, protector).GetAwaiter().GetResult();
    if (encrypted > 0)
        app.Logger.LogInformation("Encrypted {Count} previously unencrypted chat messages.", encrypted);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseForwardedHeaders();
if (app.Environment.IsProduction())
{
    app.UseHsts();
    app.UseHttpsRedirection();
    // Redirection can be skipped by the framework when no HTTPS port is known.
    app.Use(async (context, next) =>
    {
        if (!context.Request.IsHttps)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        await next(context);
    });
}

// CORS must run before Authentication/Authorization and before
// MapControllers — otherwise the browser's preflight (OPTIONS) request
// never gets an Access-Control-Allow-Origin header and every
// cross-origin call from the web app is silently blocked.
app.UseCors();

// Authentication must run BEFORE Authorization: authentication figures
// out WHO is calling (validates the token, builds the User object);
// authorization then decides WHAT that identity is allowed to do
// (checks the [Authorize] attribute). Reversing this order means
// Authorization would run with no identity to check yet.
app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();

app.MapControllers();

app.Run();

// Makes the implicit Program class visible to the test project's
// WebApplicationFactory<Program>, which boots this app in-memory.
public partial class Program { }
