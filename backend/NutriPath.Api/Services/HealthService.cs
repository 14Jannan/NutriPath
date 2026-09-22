using NutriPath.Api.Models;

namespace NutriPath.Api.Services;

public class HealthService : IHealthService
{
    private readonly IWebHostEnvironment _environment;

    public HealthService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public HealthStatus GetStatus()
    {
        return new HealthStatus
        {
            Status = "Healthy",
            Service = "NutriPath.Api",
            Environment = _environment.EnvironmentName,
            CheckedAtUtc = DateTime.UtcNow,
        };
    }
}
