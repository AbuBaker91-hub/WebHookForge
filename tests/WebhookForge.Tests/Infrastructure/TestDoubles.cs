using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using WebhookForge.Application.Common.Interfaces;
using WebhookForge.Domain.Enums;

namespace WebhookForge.Tests.Infrastructure;

/// <summary>
/// Deterministic stand-in for the real AI provider so the analyze endpoint can be
/// exercised end-to-end without a live provider key. Echoes the provider and a marker
/// so tests can assert the full pipeline (auth → decrypt key → fetch request → call AI) ran.
/// </summary>
public sealed class StubAiAnalysisService : IAiAnalysisService
{
    public const string Marker = "STUB_ANALYSIS_OK";

    public Task<string> AnalyzeWebhookAsync(
        AiProvider provider, string apiKey, string method, string? path,
        string? headers, string? body, CancellationToken ct = default)
        => Task.FromResult($"{Marker} provider={provider} method={method}");
}

/// <summary>
/// Test-only middleware that lets a request spoof its client IP via the X-Test-IP header.
/// Registered as an IStartupFilter so it runs before UseRateLimiter, enabling the
/// per-IP rate-limit partition to be verified deterministically.
/// </summary>
public sealed class TestClientIpStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (ctx, nxt) =>
        {
            if (ctx.Request.Headers.TryGetValue("X-Test-IP", out var ip) &&
                IPAddress.TryParse(ip.ToString(), out var parsed))
            {
                ctx.Connection.RemoteIpAddress = parsed;
            }
            await nxt();
        });
        next(app);
    };
}
