using NutriPath.Api.Models;

namespace NutriPath.Api.Services;

public interface IJwtTokenService
{
    (string token, DateTime expiresAtUtc) CreateAccessToken(User user);
    string CreateRefreshToken();
    string HashRefreshToken(string refreshToken);
}