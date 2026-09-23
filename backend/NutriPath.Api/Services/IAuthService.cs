using NutriPath.Api.DTOs;

namespace NutriPath.Api.Services;

public interface IAuthService
{
    Task RegisterAsync(RegisterRequest request);
    Task VerifyOtpAsync(VerifyOtpRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<AuthResponse> RefreshAsync(RefreshRequest request);
    Task ForgotPasswordAsync(ForgotPasswordRequest request);
    Task ResetPasswordAsync(ResetPasswordRequest request);
}