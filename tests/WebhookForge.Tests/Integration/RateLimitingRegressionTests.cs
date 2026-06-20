using System.Net;
using System.Text;
using WebhookForge.Tests.Infrastructure;
using Xunit;

namespace WebhookForge.Tests.Integration;

/// <summary>
/// Regression for the interviewer's headline finding: the webhook rate limiter must be
/// partitioned PER CLIENT IP, so one abusive IP cannot exhaust the limit for everyone.
/// (Before the fix it was a single global 120/min bucket.)
/// IP is spoofed via the X-Test-IP header (see TestClientIpStartupFilter).
/// </summary>
public class RateLimitingRegressionTests : IClassFixture<WebhookForgeApiFactory>
{
    private const int Limit = 120;
    private readonly WebhookForgeApiFactory _factory;
    public RateLimitingRegressionTests(WebhookForgeApiFactory factory) => _factory = factory;

    private static HttpRequestMessage Hook(string ip) =>
        new(HttpMethod.Post, "/hooks/any-token")
        {
            Headers = { { "X-Test-IP", ip } },
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };

    [Fact] // TC-RATE-01 — a single IP gets throttled after its own quota
    public async Task SingleIp_IsThrottled_AfterLimit()
    {
        var client = _factory.CreateClient();
        const string ip = "203.0.113.10";

        // The first `Limit` requests pass the limiter (controller then 404s on the unknown token).
        for (var i = 0; i < Limit; i++)
        {
            var ok = await client.SendAsync(Hook(ip));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, ok.StatusCode);
        }

        // The next one exceeds this IP's window and must be rejected.
        var throttled = await client.SendAsync(Hook(ip));
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
    }

    [Fact] // TC-RATE-02 — one IP exhausting its quota does NOT affect a different IP
    public async Task ExhaustedIp_DoesNotThrottleOtherIp()
    {
        var client = _factory.CreateClient();
        const string noisy = "203.0.113.20";
        const string quiet = "203.0.113.21";

        // Exhaust the noisy IP's quota (+1 to trip it).
        for (var i = 0; i <= Limit; i++)
            await client.SendAsync(Hook(noisy));

        var noisyResult = await client.SendAsync(Hook(noisy));
        Assert.Equal(HttpStatusCode.TooManyRequests, noisyResult.StatusCode);

        // A different IP still has its own full quota.
        var quietResult = await client.SendAsync(Hook(quiet));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, quietResult.StatusCode);
    }
}
