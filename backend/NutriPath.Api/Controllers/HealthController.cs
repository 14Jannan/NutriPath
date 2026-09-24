using Microsoft.AspNetCore.Mvc;
using NutriPath.Api.Services;

namespace NutriPath.Api.Controllers;

/// <summary>Liveness check.</summary>
[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly IHealthService _healthService;

    public HealthController(IHealthService healthService)
    {
        _healthService = healthService;
    }

    /// <summary>Reports that the API is running.</summary>
    [HttpGet]
    public IActionResult Get()
    {
        var status = _healthService.GetStatus();
        return Ok(status);
    }
}