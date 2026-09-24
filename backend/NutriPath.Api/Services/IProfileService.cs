using NutriPath.Api.DTOs;

namespace NutriPath.Api.Services;

public interface IProfileService
{
    Task<ProfileResponse> GetMyProfileAsync(Guid userId);
    Task<ProfileResponse> UpdateGoalsAsync(Guid userId, UpdateGoalsRequest request);

    /// <summary>Validates and calculates targets for unsaved values. Throws ArgumentException if invalid.</summary>
    GoalsPreviewResponse Preview(UpdateGoalsRequest request);
}
