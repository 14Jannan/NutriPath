using NutriPath.Api.DTOs;

namespace NutriPath.Api.Services;

public interface IProfileService
{
    Task<ProfileResponse> GetMyProfileAsync(Guid userId);
    Task<ProfileResponse> UpdateGoalsAsync(Guid userId, UpdateGoalsRequest request);

    /// <summary>Sets the avatar. Throws ArgumentException if it isn't one of the fixed avatars.</summary>
    Task<ProfileResponse> UpdateAvatarAsync(Guid userId, string avatarId);

    /// <summary>Validates and calculates targets for unsaved values. Throws ArgumentException if invalid.</summary>
    GoalsPreviewResponse Preview(UpdateGoalsRequest request);
}
