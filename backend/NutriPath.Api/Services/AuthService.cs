using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.DTOs;
using NutriPath.Api.Models;

namespace NutriPath.Api.Services;

public class AuthService : IAuthService
{
    private readonly NutriPathDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        NutriPathDbContext db,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        ILogger<AuthService> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
    }

    public async Task RegisterAsync(RegisterRequest request)
    {
        var emailExists = await _db.Users.AnyAsync(u => u.Email == request.Email);
        if (emailExists)
            throw new InvalidOperationException("An account with this email already exists.");

        var user = new User
        {
            Email = request.Email,
            PasswordHash = _passwordHasher.Hash(request.Password),
        };

        var code = GenerateOtpCode();
        user.EmailVerificationCode = code;
        user.EmailVerificationCodeExpiresAtUtc = DateTime.UtcNow.AddMinutes(10);

        // A UserProfile is created now with just the name captured at
        // signup; everything else (goals, targets) gets filled in Phase 8.
        user.Profile = new UserProfile { UserId = user.Id, FullName = request.FullName };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // No email provider is wired up yet — logging the code is our
        // stand-in so registration is fully testable end to end. This is
        // the one line Phase-later replaces with a real email send.
        _logger.LogWarning("EMAIL VERIFICATION CODE for {Email}: {Code}", request.Email, code);
    }

    public async Task VerifyOtpAsync(VerifyOtpRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email)
            ?? throw new InvalidOperationException("Account not found.");

        if (user.EmailVerificationCode != request.Code ||
            user.EmailVerificationCodeExpiresAtUtc < DateTime.UtcNow)
        {
            throw new InvalidOperationException("Invalid or expired code.");
        }

        user.EmailVerified = true;
        user.EmailVerificationCode = null;
        user.EmailVerificationCodeExpiresAtUtc = null;
        await _db.SaveChangesAsync();
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await _db.Users.Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Email == request.Email)
            ?? throw new UnauthorizedAccessException("Invalid email or password.");

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid email or password.");

        if (!user.EmailVerified)
            throw new UnauthorizedAccessException("Please verify your email before logging in.");

        return await IssueTokensAsync(user);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request)
    {
        var hashedIncoming = _jwtTokenService.HashRefreshToken(request.RefreshToken);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.RefreshTokenHash == hashedIncoming)
            ?? throw new UnauthorizedAccessException("Invalid refresh token.");

        if (user.RefreshTokenExpiresAtUtc < DateTime.UtcNow)
            throw new UnauthorizedAccessException("Refresh token expired. Please log in again.");

        // Rotation: issue a brand new refresh token and invalidate this
        // one, rather than reusing it. If a stolen refresh token is ever
        // used by an attacker after the real user already refreshed,
        // this old one simply won't match anymore — an early warning
        // sign worth logging/alerting on in a production system.
        return await IssueTokensAsync(user);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
        if (user == null) return; // Don't reveal whether an email exists.

        var code = GenerateOtpCode();
        user.PasswordResetCode = code;
        user.PasswordResetCodeExpiresAtUtc = DateTime.UtcNow.AddMinutes(10);
        await _db.SaveChangesAsync();

        _logger.LogWarning("PASSWORD RESET CODE for {Email}: {Code}", request.Email, code);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email)
            ?? throw new InvalidOperationException("Account not found.");

        if (user.PasswordResetCode != request.Code ||
            user.PasswordResetCodeExpiresAtUtc < DateTime.UtcNow)
        {
            throw new InvalidOperationException("Invalid or expired code.");
        }

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        user.PasswordResetCode = null;
        user.PasswordResetCodeExpiresAtUtc = null;

        // Force re-login everywhere — a password reset should invalidate
        // any existing session, in case the reset was prompted by a
        // compromised account.
        user.RefreshTokenHash = null;
        user.RefreshTokenExpiresAtUtc = null;

        await _db.SaveChangesAsync();
    }

    private async Task<AuthResponse> IssueTokensAsync(User user)
    {
        var (accessToken, expiresAtUtc) = _jwtTokenService.CreateAccessToken(user);
        var refreshToken = _jwtTokenService.CreateRefreshToken();

        user.RefreshTokenHash = _jwtTokenService.HashRefreshToken(refreshToken);
        user.RefreshTokenExpiresAtUtc = DateTime.UtcNow.AddDays(30);
        await _db.SaveChangesAsync();

        return new AuthResponse(
            accessToken,
            refreshToken,
            expiresAtUtc,
            user.Id,
            user.Email,
            user.Profile?.FullName ?? string.Empty);
    }

    private static string GenerateOtpCode() => Random.Shared.Next(100000, 999999).ToString();
}