using System.Collections.Concurrent;
using System.Net;
using System.Text;
using WebhookForge.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace WebhookForge.Tests.Integration;

/// <summary>
/// Rate-limiter behavior under genuine concurrency (the earlier regression tests fire
/// sequentially). Proves the per-IP partition holds when many requests race in parallel —
/// the scenario an abusive client actually creates.
/// </summary>
public class RateLimitConcurrencyTests : IClassFixture<WebhookForgeApiFactory>
{
    private const int Limit = 120;
    private readonly WebhookForgeApiFactory _factory;
    private readonly ITestOutputHelper _output;

    public RateLimitConcurrencyTests(WebhookForgeApiFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    private static HttpRequestMessage Hook(string ip) =>
        new(HttpMethod.Post, "/hooks/any-token")
        {
            Headers = { { "X-Test-IP", ip } },
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };

    [Fact] // TC-RATE-03 — under a parallel burst, at most `Limit` pass and the rest are 429 (no over-admission)
    public async Task ParallelBurst_AdmitsAtMostLimit_RestThrottled()
    {
        var client = _factory.CreateClient();
        const string ip = "198.51.100.5";
        const int burst = Limit + 80; // 200 simultaneous requests from one IP

        var statuses = new ConcurrentBag<HttpStatusCode>();
        await Parallel.ForEachAsync(
            Enumerable.Range(0, burst),
            new ParallelOptions { MaxDegreeOfParallelism = 64 },
            async (_, ct) =>
            {
                var res = await client.SendAsync(Hook(ip), ct);
                statuses.Add(res.StatusCode);
            });

        var admitted = statuses.Count(s => s != HttpStatusCode.TooManyRequests);
        var throttled = statuses.Count(s => s == HttpStatusCode.TooManyRequests);
        _output.WriteLine($"burst={burst} admitted={admitted} throttled(429)={throttled}");

        Assert.Equal(burst, statuses.Count);
        Assert.True(admitted <= Limit, $"Over-admitted under concurrency: {admitted} > {Limit}");
        Assert.True(throttled >= burst - Limit, $"Expected >= {burst - Limit} throttled, got {throttled}");
    }

    [Fact] // TC-RATE-04 — a parallel flood from one IP does not steal quota from another IP
    public async Task ParallelFloodOnOneIp_DoesNotThrottleAnotherIp()
    {
        var client = _factory.CreateClient();
        const string noisy = "198.51.100.10";
        const string quiet = "198.51.100.11";

        // Flood the noisy IP well past its limit, concurrently.
        await Parallel.ForEachAsync(
            Enumerable.Range(0, Limit + 60),
            new ParallelOptions { MaxDegreeOfParallelism = 64 },
            async (_, ct) => { await client.SendAsync(Hook(noisy), ct); });

        // The quiet IP, hit concurrently, should never be throttled (it has its own pool).
        var quietStatuses = new ConcurrentBag<HttpStatusCode>();
        await Parallel.ForEachAsync(
            Enumerable.Range(0, 20),
            new ParallelOptions { MaxDegreeOfParallelism = 16 },
            async (_, ct) =>
            {
                var res = await client.SendAsync(Hook(quiet), ct);
                quietStatuses.Add(res.StatusCode);
            });

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, quietStatuses);
    }
}
