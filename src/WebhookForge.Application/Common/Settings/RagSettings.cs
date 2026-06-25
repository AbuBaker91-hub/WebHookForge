namespace WebhookForge.Application.Common.Settings;

/// <summary>
/// Bound from the "Rag" configuration section. Controls the embedding model and
/// retrieval defaults for the pgvector-backed RAG pipeline.
/// The vector-store connection string lives under ConnectionStrings:RagVectorStore.
/// </summary>
public class RagSettings
{
    /// <summary>API key for the embeddings provider (OpenAI). Falls back to the OPENAI_API_KEY env var.</summary>
    public string? EmbeddingApiKey { get; set; }

    /// <summary>Embedding model id. Default: text-embedding-3-small.</summary>
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    /// <summary>Vector dimensionality. Must match the pgvector column type. Default: 1536.</summary>
    public int EmbeddingDimensions { get; set; } = 1536;

    /// <summary>Default number of chunks to retrieve when the caller doesn't specify TopK.</summary>
    public int DefaultTopK { get; set; } = 5;

    /// <summary>Chunk window size in characters.</summary>
    public int ChunkSize { get; set; } = 1800;

    /// <summary>Overlap between adjacent chunks, in characters (preserves context across boundaries).</summary>
    public int ChunkOverlap { get; set; } = 200;
}
