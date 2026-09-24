using NutriPath.Api.DTOs;

namespace NutriPath.Api.Services;

public interface IProfileService
{
    Task<ProfileResponse> GetMyProfileAsync(Guid userId);
    Task<ProfileResponse> UpdateGoalsAsync(Guid userId, UpdateGoalsRequest request);
}
