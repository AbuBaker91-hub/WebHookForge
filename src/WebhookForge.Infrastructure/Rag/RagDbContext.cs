using Microsoft.EntityFrameworkCore;

namespace WebhookForge.Infrastructure.Rag;

/// <summary>
/// Dedicated EF Core context for the PostgreSQL + pgvector vector store.
///
/// Why a second context/database? The transactional data lives in SQL Server; vector
/// similarity search is a different workload with its own indexing needs (HNSW) and is
/// best served by a purpose-built store. Keeping it isolated means the RAG feature can
/// be scaled, re-indexed, or dropped without touching the core OLTP database.
/// </summary>
public class RagDbContext : DbContext
{
    public RagDbContext(DbContextOptions<RagDbContext> options) : base(options) { }

    public DbSet<RequestChunk> Chunks => Set<RequestChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Emits "CREATE EXTENSION IF NOT EXISTS vector" in the migration.
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<RequestChunk>(builder =>
        {
            builder.ToTable("request_chunks");
            builder.HasKey(c => c.Id);

            builder.Property(c => c.Content).IsRequired();
            builder.Property(c => c.Method).IsRequired().HasMaxLength(10);
            builder.Property(c => c.Path).HasMaxLength(2048);

            // Fixed-dimension vector column (must match IEmbeddingService.Dimensions).
            builder.Property(c => c.Embedding)
                   .HasColumnType("vector(1536)")
                   .IsRequired();

            // Scope every retrieval query to one endpoint.
            builder.HasIndex(c => c.EndpointId)
                   .HasDatabaseName("ix_request_chunks_endpoint");

            // Lets ingestion delete-and-replace a single request's chunks cheaply.
            builder.HasIndex(c => c.IncomingRequestId)
                   .HasDatabaseName("ix_request_chunks_request");

            // Approximate-nearest-neighbour index for cosine similarity search.
            builder.HasIndex(c => c.Embedding)
                   .HasMethod("hnsw")
                   .HasOperators("vector_cosine_ops")
                   .HasDatabaseName("ix_request_chunks_embedding_hnsw");
        });
    }
}
