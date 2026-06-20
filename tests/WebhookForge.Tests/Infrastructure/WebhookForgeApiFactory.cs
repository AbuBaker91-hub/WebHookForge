using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebhookForge.Application.Common.Interfaces;
using WebhookForge.Infrastructure.Data;

namespace WebhookForge.Tests.Infrastructure;

/// <summary>
/// Boots the real API (Program.cs, full middleware pipeline) in-memory, but swaps:
///   • SQL Server  → EF Core InMemory (unique DB per factory, so test classes are isolated)
///   • Real AI provider → <see cref="StubAiAnalysisService"/>
///   • Adds the X-Test-IP middleware so the per-IP rate limiter is testable.
/// Everything else — JWT auth, Data Protection encryption, rate limiting, routing — is the real thing.
/// </summary>
public class WebhookForgeApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"webhookforge-tests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            // ── Replace SQL Server with an isolated in-memory database ──
            services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
            services.RemoveAll(typeof(ApplicationDbContext));
            services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(_dbName));

            // ── Replace the live AI provider with a deterministic stub ──
            services.RemoveAll(typeof(IAiAnalysisService));
            services.AddScoped<IAiAnalysisService, StubAiAnalysisService>();

            // ── Allow tests to spoof the client IP (runs before the rate limiter) ──
            services.AddSingleton<IStartupFilter, TestClientIpStartupFilter>();
        });
    }

    /// <summary>Run an action against a fresh DbContext scope (for arrange/assert against the DB).</summary>
    public async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await action(db);
    }
}
