using WebhookForge.Domain.Enums;
using WebhookForge.Infrastructure.Services;
using Xunit;

namespace WebhookForge.Tests.Live;

/// <summary>
/// OPT-IN live smoke tests that call the REAL AI providers end-to-end (real outbound HTTPS).
/// Each case runs only when its provider key env var is set; otherwise it is reported as
/// SKIPPED — so CI and local runs without keys stay green and make no network calls.
///
/// Set any/all of these and re-run to exercise the real integration:
///   $env:WEBHOOKFORGE_GROQ_KEY   = '...'   # free tier: console.groq.com
///   $env:WEBHOOKFORGE_GEMINI_KEY = '...'   # free tier: aistudio.google.com
///   $env:WEBHOOKFORGE_CLAUDE_KEY = '...'   # console.anthropic.com
///   dotnet test --filter "FullyQualifiedName~Live"
///
/// This is the automated counterpart to the manual "configure a key and click Analyze"
/// smoke test, and it validates the real request shape (header auth) + response parsing.
/// </summary>
public class LiveAiProviderTests
{
    // A realistic Stripe-style webhook so the model has something concrete to summarize.
    private const string SampleHeaders = """{"Stripe-Signature":"t=1700000000,v1=deadbeef","Content-Type":"application/json"}""";
    private const string SampleBody =
        """{"id":"evt_1","type":"payment_intent.succeeded","data":{"object":{"amount":2000,"currency":"usd"}}}""";

    [SkippableTheory]
    [InlineData(AiProvider.Groq,   "WEBHOOKFORGE_GROQ_KEY")]
    [InlineData(AiProvider.Gemini, "WEBHOOKFORGE_GEMINI_KEY")]
    [InlineData(AiProvider.Claude, "WEBHOOKFORGE_CLAUDE_KEY")]
    public async Task Analyze_WithRealProvider_ReturnsPlausibleSummary(AiProvider provider, string envVar)
    {
        var key = Environment.GetEnvironmentVariable(envVar);
        Skip.If(string.IsNullOrWhiteSpace(key), $"{envVar} not set — skipping live {provider} call.");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var svc = new AiAnalysisService(http);

        var result = await svc.AnalyzeWebhookAsync(
            provider, key!, "POST", "/webhooks/stripe", SampleHeaders, SampleBody);

        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.DoesNotContain("returned an empty response", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Unknown AI provider", result, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Length > 20, $"Suspiciously short response from {provider}: '{result}'");
    }
}
