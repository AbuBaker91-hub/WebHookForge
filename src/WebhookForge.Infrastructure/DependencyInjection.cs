using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebhookForge.Application.Common.Interfaces;
using WebhookForge.Domain.Entities;
using WebhookForge.Infrastructure.Data;
using WebhookForge.Infrastructure.Repositories;
using WebhookForge.Infrastructure.Services;
using WebhookForge.Application.Common.Settings;

namespace WebhookForge.Infrastructure;

/// <summary>
/// Extension method that registers all Infrastructure-layer services with the DI container.
/// Called from Program.cs via builder.Services.AddInfrastructure(configuration).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── In-process cache (token → endpoint hot path) ─────────
        services.AddMemoryCache();

        // ── EF Core ──────────────────────────────────────────────
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sql => sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        // ── Settings ─────────────────────────────────────────────
        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));

        // ── Generic repository (used directly for RefreshToken in UoW) ──
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

        // ── Entity-specific repositories ─────────────────────────
        services.AddScoped<IUserRepository,      UserRepository>();
        services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
        services.AddScoped<IEndpointRepository,  EndpointRepository>();
        services.AddScoped<IRequestRepository,   RequestRepository>();
        services.AddScoped<IMockRuleRepository,  MockRuleRepository>();

        // ── Unit of Work ─────────────────────────────────────────
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // ── Token service ─────────────────────────────────────────
        services.AddScoped<ITokenService, TokenService>();

        // ── AI Analysis service (Claude / Gemini / Groq) ─────────
        services.AddScoped<IAiAnalysisService, AiAnalysisService>();

        return services;
    }
}
