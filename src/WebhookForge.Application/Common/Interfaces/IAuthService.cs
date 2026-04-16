using WebhookForge.Application.Common.Models;
using WebhookForge.Application.DTOs.Auth;
using WebhookForge.Domain.Enums;

namespace WebhookForge.Application.Common.Interfaces;

/// <summary>
/// Handles user registration, login, token refresh, revocation, and profile retrieval.
/// Source: Application layer — implemented in Application/Services/AuthService.cs.
/// All methods return <see cref="Result{T}"/> so controllers stay exception-free.
/// </summary>
public interface IAuthService
{
    /// <summary>Register a new user. Fails if the email is already taken.</summary>
    Task<Result<AuthResponseDto>> RegisterAsync(RegisterDto dto, CancellationToken ct = default);

    /// <summary>Validate credentials and issue tokens. Fails if email/password are wrong or account inactive.</summary>
    Task<Result<AuthResponseDto>> LoginAsync(LoginDto dto, CancellationToken ct = default);

    /// <summary>
    /// Rotate a refresh token: revoke the old one and issue a new access + refresh token pair.
    /// Fails if the token is invalid, expired, or already revoked.
    /// </summary>
    Task<Result<AuthResponseDto>> RefreshTokenAsync(RefreshTokenDto dto, CancellationToken ct = default);

    /// <summary>Mark a refresh token as revoked (logout). Succeeds even if token is already revoked.</summary>
    Task<Result> RevokeTokenAsync(string token, CancellationToken ct = default);

    /// <summary>Return the authenticated user's profile. Fails if userId is not found.</summary>
    Task<Result<UserInfoDto>> GetProfileAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Save the user's AI provider choice and API key. Pass null to clear.</summary>
    Task<Result> SaveAiSettingsAsync(Guid userId, AiProvider? provider, string? apiKey, CancellationToken ct = default);

    /// <summary>Return the user's AI provider and key. Both null if not configured.</summary>
    Task<(AiProvider? Provider, string? ApiKey)> GetAiSettingsAsync(Guid userId, CancellationToken ct = default);
}
