using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebhookForge.Application.Common.Interfaces;
using WebhookForge.Application.DTOs.Auth;
using WebhookForge.Domain.Enums;


namespace WebhookForge.API.Controllers;

/// <summary>
/// Authentication endpoints: register, login, refresh, revoke, and profile.
/// No workspace/endpoint permission checks here — only identity concerns.
/// Thin: delegates all logic to IAuthService.
/// </summary>
public class AuthController : BaseController
{
    private readonly IAuthService _auth;
    public AuthController(IAuthService auth) => _auth = auth;

    /// <summary>Create a new account. Returns access + refresh tokens on success.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto, CancellationToken ct)
        => ToActionResult(await _auth.RegisterAsync(dto, ct));

    /// <summary>Login with email + password. Returns access + refresh tokens on success.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginDto dto, CancellationToken ct)
        => ToActionResult(await _auth.LoginAsync(dto, ct));

    /// <summary>Exchange a refresh token for a new access + refresh token pair.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenDto dto, CancellationToken ct)
        => ToActionResult(await _auth.RefreshTokenAsync(dto, ct));

    /// <summary>Revoke a refresh token (logout). Idempotent.</summary>
    [HttpPost("revoke")]
    [Authorize]
    public async Task<IActionResult> Revoke([FromBody] RevokeTokenDto dto, CancellationToken ct)
        => ToActionResult(await _auth.RevokeTokenAsync(dto.Token, ct));

    /// <summary>Return the current authenticated user's profile.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken ct)
        => ToActionResult(await _auth.GetProfileAsync(CurrentUserId, ct));

    /// <summary>Save the user's AI provider and API key.</summary>
    [HttpPut("me/ai-settings")]
    [Authorize]
    public async Task<IActionResult> SaveAiSettings([FromBody] SaveAiSettingsDto dto, CancellationToken ct)
    {
        AiProvider? provider = Enum.TryParse<AiProvider>(dto.Provider, ignoreCase: true, out var p) ? p : null;
        return ToActionResult(await _auth.SaveAiSettingsAsync(CurrentUserId, provider, dto.ApiKey, ct));
    }
}
