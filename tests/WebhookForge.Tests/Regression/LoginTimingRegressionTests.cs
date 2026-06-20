using System.Diagnostics;
using System.Net.Http.Json;
using WebhookForge.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace WebhookForge.Tests.Regression;

/// <summary>
/// Regression for the interviewer's finding: login must run BCrypt.Verify even when the
/// email does not exist, so an attacker cannot enumerate accounts by response timing.
/// If the verify were short-circuited for unknown users, that path would return in
/// sub-millisecond time and this test would fail.
/// </summary>
public class LoginTimingRegressionTests : IClassFixture<WebhookForgeApiFactory>
{
    private readonly WebhookForgeApiFactory _factory;
    private readonly ITestOutputHelper _output;

    public LoginTimingRegressionTests(WebhookForgeApiFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Fact] // TC-TIMING-01
    public async Task UnknownUserLogin_IncursBcryptCost_LikeWrongPassword()
    {
        var client = _factory.CreateClient();
        var knownEmail = $"timing-{Guid.NewGuid():N}@example.com";
        await client.RegisterAsync(knownEmail, "Sup3rSecret!");

        async Task<double> AverageMsAsync(string email)
        {
            // warm-up (JIT / first-hit) then measure
            await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong-password" });

            const int iterations = 5;
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
                await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong-password" });
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds / iterations;
        }

        var existingUserAvg = await AverageMsAsync(knownEmail);
        var unknownUserAvg  = await AverageMsAsync($"ghost-{Guid.NewGuid():N}@example.com");

        _output.WriteLine($"existing-user wrong-pw avg: {existingUserAvg:F1} ms");
        _output.WriteLine($"unknown-user        avg: {unknownUserAvg:F1} ms");

        // BCrypt (work factor 11) costs tens of ms. A skipped verify would be ~0 ms.
        Assert.True(unknownUserAvg >= 5.0,
            $"Unknown-user login was too fast ({unknownUserAvg:F1} ms) — BCrypt.Verify appears to be skipped.");

        // And it should be in the same ballpark as the real-hash path (not an obvious oracle).
        Assert.True(unknownUserAvg >= existingUserAvg * 0.5,
            $"Unknown-user path ({unknownUserAvg:F1} ms) is far faster than existing-user path ({existingUserAvg:F1} ms).");
    }
}
