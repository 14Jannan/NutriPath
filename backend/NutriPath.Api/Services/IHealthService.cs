using NutriPath.Api.Models;

namespace NutriPath.Api.Services;

public interface IHealthService
{
    HealthStatus GetStatus();
}
