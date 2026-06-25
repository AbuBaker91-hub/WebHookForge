using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WebhookForge.Infrastructure.Rag;

/// <summary>
/// Design-time factory so "dotnet ef migrations add ... --context RagDbContext" works
/// without spinning up the full host/DI. The connection string is read from the
/// RagVectorStore env var, falling back to the local docker-compose Postgres.
/// Not used at runtime — runtime registration lives in DependencyInjection.cs.
/// </summary>
public class RagDbContextFactory : IDesignTimeDbContextFactory<RagDbContext>
{
    public RagDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("RagVectorStore")
            ?? "Host=localhost;Port=5433;Database=webhookforge_rag;Username=rag;Password=ragpass";

        var options = new DbContextOptionsBuilder<RagDbContext>()
            .UseNpgsql(connection, npgsql =>
            {
                npgsql.UseVector();
                npgsql.MigrationsAssembly(typeof(RagDbContext).Assembly.FullName);
            })
            .Options;

        return new RagDbContext(options);
    }
}
