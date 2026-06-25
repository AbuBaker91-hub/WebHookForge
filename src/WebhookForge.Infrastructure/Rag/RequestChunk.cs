using Pgvector;

namespace WebhookForge.Infrastructure.Rag;

/// <summary>
/// A single embedded chunk of a captured webhook request, persisted in the
/// pgvector store (separate Postgres database from the transactional SQL Server DB).
///
/// One <see cref="WebhookForge.Domain.Entities.IncomingRequest"/> can produce several
/// chunks (large bodies are split with overlap). Each chunk carries enough metadata
/// to cite the source request without a cross-database join.
/// </summary>
public class RequestChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The endpoint whose history this chunk belongs to (retrieval is scoped per endpoint).</summary>
    public Guid EndpointId { get; set; }

    /// <summary>The source request in the SQL Server store — used for citations.</summary>
    public Guid IncomingRequestId { get; set; }

    /// <summary>The chunk text that was embedded.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>The embedding vector (dimensionality fixed by the embedding model).</summary>
    public Vector Embedding { get; set; } = null!;

    /// <summary>Source request metadata, denormalised for fast citation rendering.</summary>
    public string  Method     { get; set; } = string.Empty;
    public string? Path       { get; set; }
    public DateTime ReceivedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
