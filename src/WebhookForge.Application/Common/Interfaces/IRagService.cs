using WebhookForge.Application.Common.Models;
using WebhookForge.Application.DTOs.Rag;
using WebhookForge.Domain.Enums;

namespace WebhookForge.Application.Common.Interfaces;

/// <summary>
/// Retrieval-augmented generation over captured webhook history.
///
/// Two phases:
///   1. <see cref="IngestEndpointAsync"/> — chunk + embed an endpoint's captured requests
///      into the pgvector store (idempotent; safe to re-run).
///   2. <see cref="AskAsync"/> — embed the question, retrieve the most similar chunks by
///      cosine distance, and have the user's chosen LLM answer grounded in that context.
///
/// Implemented in the Infrastructure layer (needs the pgvector DbContext).
/// </summary>
public interface IRagService
{
    /// <summary>
    /// Build (or rebuild) the vector index for one endpoint's captured requests.
    /// Verifies the caller has access to the endpoint's workspace.
    /// </summary>
    Task<Result<RagIngestResultDto>> IngestEndpointAsync(
        Guid endpointId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Answer a question grounded in the endpoint's indexed webhooks.
    /// The LLM provider/key come from the caller (same source as the analyze feature),
    /// so no shared server key is required.
    /// </summary>
    Task<Result<RagAnswerDto>> AskAsync(
        Guid endpointId, Guid userId, AiProvider provider, string apiKey,
        RagAskDto request, CancellationToken ct = default);
}
