using Microsoft.AspNetCore.DataProtection;
using WebhookForge.Infrastructure.Services;
using Xunit;

namespace WebhookForge.Tests.Unit;

public class ApiKeyProtectorTests
{
    private static ApiKeyProtector NewProtector() => new(new EphemeralDataProtectionProvider());

    [Fact] // TC-PROT-01
    public void Protect_ThenUnprotect_RoundTripsOriginal()
    {
        var protector = NewProtector();
        const string secret = "sk-ant-api03-abc.def.ghi";

        var cipher = protector.Protect(secret);

        Assert.NotNull(cipher);
        Assert.NotEqual(secret, cipher);                 // actually encrypted
        Assert.DoesNotContain(secret, cipher!);          // plaintext not embedded
        Assert.Equal(secret, protector.Unprotect(cipher));
    }

    [Theory] // TC-PROT-02
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Protect_NullOrBlank_ReturnsNull(string? input)
        => Assert.Null(NewProtector().Protect(input));

    [Theory] // TC-PROT-03
    [InlineData(null)]
    [InlineData("")]
    public void Unprotect_NullOrBlank_ReturnsNull(string? input)
        => Assert.Null(NewProtector().Unprotect(input));

    [Fact] // TC-PROT-04 — undecryptable input (legacy plaintext / rotated key) is handled, not thrown
    public void Unprotect_GarbageOrLegacyPlaintext_ReturnsNull()
    {
        var protector = NewProtector();
        Assert.Null(protector.Unprotect("this-is-not-a-protected-payload"));
    }

    [Fact] // TC-PROT-05 — a key created by one keyring cannot be read by another
    public void Unprotect_WithDifferentKeyring_ReturnsNull()
    {
        var cipher = NewProtector().Protect("secret-value");
        var other = NewProtector(); // independent ephemeral keyring
        Assert.Null(other.Unprotect(cipher));
    }
}
