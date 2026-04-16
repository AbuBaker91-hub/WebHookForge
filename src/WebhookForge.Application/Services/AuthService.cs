using WebhookForge.Application.Common.Interfaces;
using WebhookForge.Application.Common.Models;
using WebhookForge.Application.DTOs.Auth;
using WebhookForge.Application.Common.Settings;
using WebhookForge.Domain.Entities;
using WebhookForge.Domain.Enums;
using Microsoft.Extensions.Options;

namespace WebhookForge.Application.Services;

/// <summary>
/// Handles user registration, login, token refresh, revocation, and profile retrieval.
/// All auth decisions are made here; controllers are kept thin.
/// Depends on: IUnitOfWork, ITokenService, JwtSettings.
/// </summary>
public class AuthService : IAuthService
{
    private readonly IUnitOfWork   _uow;
    private readonly ITokenService _tokens;
    private readonly JwtSettings   _jwt;

    public AuthService(IUnitOfWork uow, ITokenService tokens, IOptions<JwtSettings> jwt)
    {
        _uow    = uow;
        _tokens = tokens;
        _jwt    = jwt.Value;
    }

    /// <inheritdoc/>
    public async Task<Result<AuthResponseDto>> RegisterAsync(RegisterDto dto, CancellationToken ct = default)
    {
        if (await _uow.Users.EmailExistsAsync(dto.Email, ct))
            return Result<AuthResponseDto>.Failure("An account with this email already exists.");

        var user = new User
        {
            Email        = dto.Email.ToLowerInvariant().Trim(),
            DisplayName  = dto.DisplayName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password)
        };

        await _uow.Users.AddAsync(user, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<AuthResponseDto>.Success(await BuildAuthResponseAsync(user, ct));
    }

    /// <inheritdoc/>
    public async Task<Result<AuthResponseDto>> LoginAsync(LoginDto dto, CancellationToken ct = default)
    {
        var user = await _uow.Users.GetByEmailAsync(dto.Email, ct);

        // Constant-time comparison: always verify even on missing user to resist timing attacks
        var passwordOk = user is not null
                      && BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);

        if (!passwordOk || user is null)
            return Result<AuthResponseDto>.Unauthorized("Invalid email or password.");

        if (!user.IsActive)
            return Result<AuthResponseDto>.Unauthorized("This account has been deactivated.");

        user.LastLoginAt = DateTime.UtcNow;
        await _uow.Users.UpdateAsync(user, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<AuthResponseDto>.Success(await BuildAuthResponseAsync(user, ct));
    }

    /// <inheritdoc/>
    public async Task<Result<AuthResponseDto>> RefreshTokenAsync(RefreshTokenDto dto, CancellationToken ct = default)
    {
        var token = await _uow.Users.GetActiveRefreshTokenAsync(dto.RefreshToken, ct);
        if (token is null)
            return Result<AuthResponseDto>.Unauthorized("Invalid or expired refresh token.");

        // Rotate: revoke old token, issue new pair
        token.IsRevoked = true;
        await _uow.SaveChangesAsync(ct);

        return Result<AuthResponseDto>.Success(await BuildAuthResponseAsync(token.User, ct));
    }

    /// <inheritdoc/>
    public async Task<Result> RevokeTokenAsync(string token, CancellationToken ct = default)
    {
        var record = await _uow.Users.GetActiveRefreshTokenAsync(token, ct);
        if (record is not null)
        {
            record.IsRevoked = true;
            await _uow.SaveChangesAsync(ct);
        }
        return Result.Success(); // Idempotent — already revoked is still success
    }

    /// <inheritdoc/>
    public async Task<Result<UserInfoDto>> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _uow.Users.GetByIdAsync(userId, ct);
        return user is null
            ? Result<UserInfoDto>.NotFound("User not found.")
            : Result<UserInfoDto>.Success(ToUserInfo(user));
    }

    // ── Private helpers ──────────────────────────────────────────

    /// <summary>
    /// Create a new access + refresh token pair, persist the refresh token, and build the response DTO.
    /// </summary>
    private async Task<AuthResponseDto> BuildAuthResponseAsync(User user, CancellationToken ct)
    {
        var accessToken    = _tokens.GenerateAccessToken(user);
        var refreshRaw     = _tokens.GenerateRefreshToken();

        await _uow.RefreshTokens.AddAsync(new RefreshToken
        {
            UserId    = user.Id,
            Token     = refreshRaw,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwt.RefreshTokenExpiryDays)
        }, ct);

        await _uow.SaveChangesAsync(ct);

        return new AuthResponseDto
        {
            AccessToken  = accessToken,
            RefreshToken = refreshRaw,
            ExpiresAt    = DateTime.UtcNow.AddMinutes(_jwt.AccessTokenExpiryMinutes),
            User         = ToUserInfo(user)
        };
    }

    /// <inheritdoc/>
    public async Task<Result> SaveAiSettingsAsync(Guid userId, AiProvider? provider, string? apiKey, CancellationToken ct = default)
    {
        var user = await _uow.Users.GetByIdAsync(userId, ct);
        if (user is null) return Result.NotFound("User not found.");

        user.AiProvider = provider;
        user.AiApiKey   = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        await _uow.Users.UpdateAsync(user, ct);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <inheritdoc/>
    public async Task<(AiProvider? Provider, string? ApiKey)> GetAiSettingsAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _uow.Users.GetByIdAsync(userId, ct);
        return (user?.AiProvider, user?.AiApiKey);
    }

    private static UserInfoDto ToUserInfo(User u) => new()
    {
        Id          = u.Id,
        Email       = u.Email,
        DisplayName = u.DisplayName,
        AiProvider  = u.AiProvider?.ToString()
    };
}
