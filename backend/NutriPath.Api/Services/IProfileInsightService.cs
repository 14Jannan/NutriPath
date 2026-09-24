using NutriPath.Api.DTOs;

namespace NutriPath.Api.Services;

public interface IProfileInsightService
{
    /// <summary>
    /// Calculates targets for the (unsaved) values, then asks the AI to explain
    /// what they mean for this person. Throws ArgumentException if values are invalid.
    /// </summary>
    Task<ProfileInsightResponse> GetInsightAsync(UpdateGoalsRequest request);
}
