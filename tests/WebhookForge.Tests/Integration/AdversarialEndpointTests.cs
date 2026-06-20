using System.Net;
using System.Net.Http.Json;
using System.Text;
using WebhookForge.Tests.Infrastructure;
using Xunit;

namespace WebhookForge.Tests.Integration;

/// <summary>
/// Abuse / manipulation of the public webhook endpoint: malformed and oversized payloads,
/// odd methods, and token tampering. The receiver should degrade gracefully (4xx, never 5xx)
/// and still capture what it accepts.
/// </summary>
public class AdversarialEndpointTests : IClassFixture<WebhookForgeApiFactory>
{
    private readonly WebhookForgeApiFactory _factory;
    public AdversarialEndpointTests(WebhookForgeApiFactory factory) => _factory = factory;

    private async Task<EndpointResponse> NewEndpointAsync()
    {
        var client = _factory.CreateClient();
        await client.RegisterAndAuthenticateAsync();
        var ws = await client.CreateWorkspaceAsync();
        return await client.CreateEndpointAsync(ws.Id);
    }

    [Fact] // TC-ADV-01 — a large payload is captured, not rejected or crashed
    public async Task OversizedBody_IsCaptured()
    {
        var ep = await NewEndpointAsync();
        var anon = _factory.CreateClient();
        var big = new string('x', 1_000_000); // ~1 MB

        var res = await anon.PostAsync($"/hooks/{ep.Token}",
            new StringContent(big, Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact] // TC-ADV-02 — malformed JSON is stored as a raw body (capture never parses it)
    public async Task MalformedJsonBody_IsCaptured()
    {
        var ep = await NewEndpointAsync();
        var anon = _factory.CreateClient();

        var res = await anon.PostAsync($"/hooks/{ep.Token}",
            new StringContent("{not-valid-json::", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact] // TC-ADV-03 — empty body is accepted
    public async Task EmptyBody_IsAccepted()
    {
        var ep = await NewEndpointAsync();
        var anon = _factory.CreateClient();

        var res = await anon.PostAsync($"/hooks/{ep.Token}", new StringContent("", Encoding.UTF8));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Theory] // TC-ADV-04 — tampered/garbage tokens are 404s, never 500s
    [InlineData("../../etc/passwd")]
    [InlineData("' OR 1=1--")]
    [InlineData("%00%01%02")]
    public async Task TamperedToken_ReturnsClientError_NotServerError(string rawToken)
    {
        var anon = _factory.CreateClient();
        var res = await anon.PostAsync($"/hooks/{Uri.EscapeDataString(rawToken)}",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.True((int)res.StatusCode is >= 400 and < 500,
            $"Expected a 4xx for a tampered token, got {(int)res.StatusCode}");
    }

    [Fact] // TC-ADV-05 — an absurdly long token is handled (4xx, no crash)
    public async Task VeryLongToken_IsHandledGracefully()
    {
        var anon = _factory.CreateClient();
        var res = await anon.PostAsync($"/hooks/{new string('a', 4000)}",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.True((int)res.StatusCode < 500, $"Server error on long token: {(int)res.StatusCode}");
    }

    [Fact] // TC-ADV-06 — an unsupported HTTP method does not 500
    public async Task UnsupportedMethod_DoesNotServerError()
    {
        var ep = await NewEndpointAsync();
        var anon = _factory.CreateClient();

        var res = await anon.SendAsync(new HttpRequestMessage(HttpMethod.Options, $"/hooks/{ep.Token}"));

        Assert.True((int)res.StatusCode < 500, $"Server error on OPTIONS: {(int)res.StatusCode}");
    }
}

/// <summary>
/// Security regression: the per-IP limiter must key on the real connection IP. Because the app
/// does NOT trust X-Forwarded-For (no ForwardedHeaders configured), an attacker cannot reset
/// their bucket by spoofing that header. Isolated in its own fixture so it owns the limiter window.
/// </summary>
public class ForwardedHeaderSpoofTests : IClassFixture<WebhookForgeApiFactory>
{
    private readonly WebhookForgeApiFactory _factory;
    public ForwardedHeaderSpoofTests(WebhookForgeApiFactory factory) => _factory = factory;

    [Fact] // TC-ADV-07 — rotating X-Forwarded-For does not grant fresh quota
    public async Task SpoofingXForwardedFor_DoesNotBypassPerIpLimit()
    {
        var client = _factory.CreateClient();

        HttpRequestMessage Req(string xff) =>
            new(HttpMethod.Post, "/hooks/any-token")
            {
                Headers = { { "X-Forwarded-For", xff } },
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };

        // Exhaust the (single, real) partition while pretending to be many different clients.
        for (var i = 0; i < 121; i++)
            await client.SendAsync(Req($"10.0.0.{i % 255}"));

        // A brand-new spoofed IP must still be throttled — XFF was ignored.
        var res = await client.SendAsync(Req("172.16.99.99"));
        Assert.Equal(HttpStatusCode.TooManyRequests, res.StatusCode);
    }
}
