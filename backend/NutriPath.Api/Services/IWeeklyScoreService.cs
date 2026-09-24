using NutriPath.Api.DTOs;

namespace NutriPath.Api.Services;

public interface IWeeklyScoreService
{
    Task<WeeklyScoreResponse> GetCurrentWeekScoreAsync(Guid userId);
}
