using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using WebhookForge.Application.Common.Interfaces;

namespace WebhookForge.Infrastructure.Services;

/// <summary>
/// Encrypts AI provider API keys at rest using ASP.NET Core Data Protection.
/// Only the protected (encrypted) form is persisted in the Users table; the
/// plaintext key is decrypted on demand when an analysis request is made and
/// is never returned to the frontend.
/// </summary>
public class ApiKeyProtector : IApiKeyProtector
{
    private readonly IDataProtector _protector;

    public ApiKeyProtector(IDataProtectionProvider provider)
        // Purpose string ties protected payloads to this use case; changing it invalidates old values.
        => _protector = provider.CreateProtector("WebhookForge.AiApiKey.v1");

    /// <inheritdoc/>
    public string? Protect(string? plaintext)
        => string.IsNullOrWhiteSpace(plaintext) ? null : _protector.Protect(plaintext);

    /// <inheritdoc/>
    public string? Unprotect(string? ciphertext)
    {
        if (string.IsNullOrWhiteSpace(ciphertext)) return null;

        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch (CryptographicException)
        {
            // Undecryptable (legacy plaintext or rotated keyring) — treat as "not set".
            return null;
        }
    }
}
