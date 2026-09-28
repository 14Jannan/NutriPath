using NutriPath.Api.DTOs;

namespace NutriPath.Api.Services;

public interface IAiUsageService
{
    /// <summary>How much of the allowance is used, and when it frees up if it's all used.</summary>
    Task<AiUsageStatus> GetStatusAsync(Guid userId);
}
