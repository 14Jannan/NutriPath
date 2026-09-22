namespace NutriPath.Api.Models;

public class HealthStatus
{
    public string Status {get; set;}="Healthy";
    public string Service {get; set;}="NutriPath.Api";
    public string Environment {get; set;}="Development";
    public DateTime CheckedAtUtc {get; set;}
}