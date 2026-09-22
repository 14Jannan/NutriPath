using Microsoft.AspNetCore.Mvc;
using NutriPath.Api.Services;

namespace NutriPath.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly IHealthService _healthService;

    public HealthController(IHealthService healthService)
    {
        _healthService = healthService;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var status = _healthService.GetStatus();
        return Ok(status);
    }
}