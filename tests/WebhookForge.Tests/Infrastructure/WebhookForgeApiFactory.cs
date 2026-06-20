using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using WebhookForge.Application.Common.Interfaces;
using WebhookForge.Infrastructure.Data;

namespace WebhookForge.Tests.Infrastructure;

/// <summary>
/// Boots the real API (Program.cs, full middleware pipeline) in-memory, but swaps:
///   • Database → SQL Server when WEBHOOKFORGE_TEST_SQL_SERVER is set, otherwise EF Core
///               InMemory. Each factory gets its own isolated, throwaway database.
///   • Real AI provider → <see cref="StubAiAnalysisService"/>
///   • Adds the X-Test-IP middleware so the per-IP rate limiter is testable.
/// Everything else — JWT auth, Data Protection encryption, rate limiting, routing — is the real thing.
///
/// Run against the real SQL Server engine (so "test == live" for EF mappings/defaults/indexes):
///   $env:WEBHOOKFORGE_TEST_SQL_SERVER = '(localdb)\MSSQLLocalDB'; dotnet test
/// </summary>
public class WebhookForgeApiFactory : WebApplicationFactory<Program>
{
    private static readonly string? SqlServer =
        Environment.GetEnvironmentVariable("WEBHOOKFORGE_TEST_SQL_SERVER");

    private readonly string _dbName = $"WebhookForgeTest_{Guid.NewGuid():N}";
    private bool UseSql => !string.IsNullOrWhiteSpace(SqlServer);

    private string SqlConnectionString =>
        $"Server={SqlServer};Database={_dbName};Trusted_Connection=True;TrustServerCertificate=True;";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            // ── Swap the database provider for an isolated test database ──
            services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
            services.RemoveAll(typeof(ApplicationDbContext));

            if (UseSql)
                services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(SqlConnectionString));
            else
                services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(_dbName));

            // ── Replace the live AI provider with a deterministic stub ──
            services.RemoveAll(typeof(IAiAnalysisService));
            services.AddScoped<IAiAnalysisService, StubAiAnalysisService>();

            // ── Allow tests to spoof the client IP (runs before the rate limiter) ──
            services.AddSingleton<IStartupFilter, TestClientIpStartupFilter>();
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        if (UseSql)
        {
            using var scope = host.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();
        }
        return host;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && UseSql)
        {
            try
            {
                using var scope = Services.CreateScope();
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureDeleted();
            }
            catch { /* best-effort cleanup of the throwaway test database */ }
        }
        base.Dispose(disposing);
    }

    /// <summary>Run an action against a fresh DbContext scope (for arrange/assert against the DB).</summary>
    public async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await action(db);
    }
}
