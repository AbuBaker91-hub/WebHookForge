namespace WebhookForge.Application.Common.Interfaces;

/// <summary>
/// Turns text into dense vector embeddings for semantic search.
/// Implemented against OpenAI's embeddings API (text-embedding-3-small, 1536 dims) by default.
/// Kept provider-agnostic so the model can be swapped without touching the RAG pipeline.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>The dimensionality of the vectors this service produces (must match the pgvector column).</summary>
    int Dimensions { get; }

    /// <summary>Embed a single piece of text.</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Embed many texts in one round-trip (the embeddings API accepts a batch input array).
    /// Order of the returned vectors matches the order of <paramref name="texts"/>.
    /// </summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
