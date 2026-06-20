using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using WebhookForge.Tests.Infrastructure;
using Xunit;

namespace WebhookForge.Tests.Integration;

public class AuthApiTests : IClassFixture<WebhookForgeApiFactory>
{
    private readonly WebhookForgeApiFactory _factory;
    public AuthApiTests(WebhookForgeApiFactory factory) => _factory = factory;

    [Fact] // TC-AUTH-01
    public async Task Register_ReturnsTokensAndUser()
    {
        var client = _factory.CreateClient();
        var auth = await client.RegisterAsync(display: "Alice");

        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));
        Assert.Equal("Alice", auth.User.DisplayName);
    }

    [Fact] // TC-AUTH-02
    public async Task Register_DuplicateEmail_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var email = $"dup-{Guid.NewGuid():N}@example.com";
        await client.RegisterAsync(email);

        var res = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "Sup3rSecret!", displayName = "Dup" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact] // TC-AUTH-03
    public async Task Login_WithValidCredentials_Succeeds()
    {
        var client = _factory.CreateClient();
        var email = $"login-{Guid.NewGuid():N}@example.com";
        await client.RegisterAsync(email, "Sup3rSecret!");

        var res = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Sup3rSecret!" });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var auth = await res.ReadAsync<AuthResponse>();
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
    }

    [Fact] // TC-AUTH-04
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var email = $"wrongpw-{Guid.NewGuid():N}@example.com";
        await client.RegisterAsync(email, "Sup3rSecret!");

        var res = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "totally-wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact] // TC-AUTH-05 — user-enumeration protection: identical response for unknown vs wrong-password
    public async Task Login_UnknownUser_ReturnsSameGenericMessageAsWrongPassword()
    {
        var client = _factory.CreateClient();
        var known = $"known-{Guid.NewGuid():N}@example.com";
        await client.RegisterAsync(known, "Sup3rSecret!");

        var wrongPw = await client.PostAsJsonAsync("/api/auth/login", new { email = known, password = "nope" });
        var unknown = await client.PostAsJsonAsync("/api/auth/login",
            new { email = $"ghost-{Guid.NewGuid():N}@example.com", password = "nope" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPw.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await wrongPw.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [Fact] // TC-AUTH-06
    public async Task Me_WithoutToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact] // TC-AUTH-07
    public async Task Me_WithToken_ReturnsProfile()
    {
        var client = _factory.CreateClient();
        var auth = await client.RegisterAndAuthenticateAsync();

        var res = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var me = await res.ReadAsync<UserInfo>();
        Assert.Equal(auth.User.Id, me.Id);
    }

    [Fact] // TC-AUTH-08 — refresh token rotation: old token is invalid after refresh
    public async Task Refresh_RotatesToken_OldTokenRejected()
    {
        var client = _factory.CreateClient();
        var auth = await client.RegisterAsync();

        var first = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = auth.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Re-using the now-rotated original token must fail.
        var reuse = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = auth.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
    }
}
