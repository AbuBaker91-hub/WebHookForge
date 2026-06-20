namespace WebhookForge.Application.Common.Interfaces;

/// <summary>
/// Encrypts and decrypts sensitive user secrets (currently AI provider API keys)
/// so they are never stored in plaintext at rest.
/// Source: implemented in Infrastructure via ASP.NET Core Data Protection.
/// </summary>
public interface IApiKeyProtector
{
    /// <summary>Encrypt a plaintext secret. Returns null when the input is null or blank.</summary>
    string? Protect(string? plaintext);

    /// <summary>
    /// Decrypt a value previously produced by <see cref="Protect"/>.
    /// Returns null when the input is blank or cannot be decrypted (e.g. legacy
    /// plaintext or a rotated key), signalling that the user must re-enter the key.
    /// </summary>
    string? Unprotect(string? ciphertext);
}
