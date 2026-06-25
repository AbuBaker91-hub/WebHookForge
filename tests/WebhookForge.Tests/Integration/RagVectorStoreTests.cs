using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using WebhookForge.Infrastructure.Rag;
using Xunit;

namespace WebhookForge.Tests.Integration;

/// <summary>
/// Real pgvector integration: validates the Npgsql + pgvector mapping and cosine-distance
/// retrieval against an actual Postgres instance — with NO paid embedding API calls
/// (vectors are hand-crafted, so this exercises the vector store, not OpenAI).
///
/// Runs only when the pgvector Postgres is reachable; otherwise SKIPPED so CI stays green:
///   docker compose -f docker-compose.rag.yml up -d
///   dotnet test --filter "FullyQualifiedName~RagVectorStore"
/// </summary>
public class RagVectorStoreTests
{
    private const int Dim = 1536;

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("RagVectorStore")
        ?? "Host=localhost;Port=5433;Database=webhookforge_rag;Username=rag;Password=ragpass";

    private static RagDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<RagDbContext>()
            .UseNpgsql(ConnectionString, o => o.UseVector())
            .Options;
        return new RagDbContext(options);
    }

    /// <summary>A 1536-dim vector with the leading components set, rest zero.</summary>
    private static Vector Vec(params float[] lead)
    {
        var arr = new float[Dim];
        Array.Copy(lead, arr, lead.Length);
        return new Vector(arr);
    }

    [SkippableFact] // TC-RAG-01 — cosine search returns the nearest chunk first
    public async Task CosineSearch_RanksByVectorSimilarity()
    {
        using var db = NewContext();
        Skip.If(!db.Database.CanConnect(),
            "pgvector Postgres not reachable — run docker-compose.rag.yml to enable this test.");
        db.Database.EnsureCreated();

        var endpointId = Guid.NewGuid();          // unique scope so the test is isolated
        var reqNear  = Guid.NewGuid();
        var reqMid   = Guid.NewGuid();
        var reqFar   = Guid.NewGuid();

        try
        {
            db.Chunks.AddRange(
                new RequestChunk { EndpointId = endpointId, IncomingRequestId = reqNear, Content = "near",
                                   Method = "POST", ReceivedAt = DateTime.UtcNow, Embedding = Vec(1f, 0f, 0f) },
                new RequestChunk { EndpointId = endpointId, IncomingRequestId = reqMid,  Content = "mid",
                                   Method = "POST", ReceivedAt = DateTime.UtcNow, Embedding = Vec(0.8f, 0.2f, 0f) },
                new RequestChunk { EndpointId = endpointId, IncomingRequestId = reqFar,  Content = "far",
                                   Method = "POST", ReceivedAt = DateTime.UtcNow, Embedding = Vec(0f, 1f, 0f) });
            await db.SaveChangesAsync();

            var query = Vec(1f, 0f, 0f);  // identical to "near"

            var ranked = await db.Chunks
                .Where(c => c.EndpointId == endpointId)
                .OrderBy(c => c.Embedding.CosineDistance(query))
                .Select(c => c.IncomingRequestId)
                .ToListAsync();

            Assert.Equal(3, ranked.Count);
            Assert.Equal(reqNear, ranked[0]);  // closest
            Assert.Equal(reqMid,  ranked[1]);  // middle
            Assert.Equal(reqFar,  ranked[2]);  // farthest
        }
        finally
        {
            await db.Chunks.Where(c => c.EndpointId == endpointId).ExecuteDeleteAsync();
        }
    }

    [SkippableFact] // TC-RAG-02 — retrieval is scoped per endpoint (no cross-endpoint leakage)
    public async Task CosineSearch_IsScopedToEndpoint()
    {
        using var db = NewContext();
        Skip.If(!db.Database.CanConnect(),
            "pgvector Postgres not reachable — run docker-compose.rag.yml to enable this test.");
        db.Database.EnsureCreated();

        var mine    = Guid.NewGuid();
        var someone = Guid.NewGuid();

        try
        {
            db.Chunks.AddRange(
                new RequestChunk { EndpointId = mine,    IncomingRequestId = Guid.NewGuid(), Content = "mine",
                                   Method = "GET", ReceivedAt = DateTime.UtcNow, Embedding = Vec(1f, 0f, 0f) },
                new RequestChunk { EndpointId = someone, IncomingRequestId = Guid.NewGuid(), Content = "theirs",
                                   Method = "GET", ReceivedAt = DateTime.UtcNow, Embedding = Vec(1f, 0f, 0f) });
            await db.SaveChangesAsync();

            var hits = await db.Chunks
                .Where(c => c.EndpointId == mine)
                .OrderBy(c => c.Embedding.CosineDistance(Vec(1f, 0f, 0f)))
                .ToListAsync();

            Assert.Single(hits);
            Assert.Equal(mine, hits[0].EndpointId);
        }
        finally
        {
            await db.Chunks.Where(c => c.EndpointId == mine || c.EndpointId == someone).ExecuteDeleteAsync();
        }
    }
}
