using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using NutriPath.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));
builder.Services.Configure<GroqSettings>(builder.Configuration.GetSection("Groq"));
builder.Services.AddScoped<IFoodSearchService, FoodSearchService>();
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

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // The mobile app is requested from a browser (web target), an
            // emulator, or a phone on the LAN, each a different origin. Auth
            // uses a Bearer header, not cookies, so AllowAnyOrigin is safe here.
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
        else
        {
            // Set to the real deployed frontend origin at deployment time.
            policy.WithOrigins(builder.Configuration["AllowedOrigin"] ?? "https://nutripath.app")
                .AllowAnyMethod()
                .AllowAnyHeader();
        }
    });
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
                PermitLimit = 5,
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
builder.Services.AddScoped<IAiService, AiService>();

// Singleton: the TF-IDF index is built once and shared by every request.
// It depends on the Scoped DbContext only via a short-lived scope.
builder.Services.AddSingleton<IKnowledgeRetrievalService, KnowledgeRetrievalService>();

builder.Services.AddHttpClient<IUsdaFoodSyncService, UsdaFoodSyncService>();
builder.Services.AddHttpClient<IGroqClient, GroqClient>();

builder.Services.AddDbContext<NutriPathDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;

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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30), // small tolerance for clock drift, not the full 5-minute .NET default
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

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
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

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
