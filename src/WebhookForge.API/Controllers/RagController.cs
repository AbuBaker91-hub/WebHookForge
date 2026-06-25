using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebhookForge.Application.Common.Interfaces;
using WebhookForge.Application.DTOs.Rag;

namespace WebhookForge.API.Controllers;

/// <summary>
/// Retrieval-augmented Q&amp;A over an endpoint's captured webhooks.
///   POST endpoints/{id}/rag/ingest  — (re)build the vector index for the endpoint.
///   POST endpoints/{id}/rag/ask     — ask a grounded question over the indexed history.
/// The LLM provider/key are the user's own (same source as the analyze feature).
/// </summary>
[Authorize]
[ApiController]
[Route("api")]
public class RagController : BaseController
{
    private readonly IRagService  _rag;
    private readonly IAuthService _auth;

    public RagController(IRagService rag, IAuthService auth)
    {
        _rag  = rag;
        _auth = auth;
    }

    /// <summary>Chunk + embed the endpoint's captured requests into the pgvector store.</summary>
    [HttpPost("endpoints/{endpointId:guid}/rag/ingest")]
    public async Task<IActionResult> Ingest(Guid endpointId, CancellationToken ct)
        => ToActionResult(await _rag.IngestEndpointAsync(endpointId, CurrentUserId, ct));

    /// <summary>Answer a natural-language question grounded in the endpoint's indexed webhooks.</summary>
    [HttpPost("endpoints/{endpointId:guid}/rag/ask")]
    public async Task<IActionResult> Ask(Guid endpointId, [FromBody] RagAskDto dto, CancellationToken ct)
    {
        var (provider, apiKey) = await _auth.GetAiSettingsAsync(CurrentUserId, ct);
        if (provider is null || string.IsNullOrEmpty(apiKey))
            return BadRequest(new { error = "No AI provider configured. Go to Settings to choose a provider and add your API key." });

        return ToActionResult(await _rag.AskAsync(endpointId, CurrentUserId, provider.Value, apiKey, dto, ct));
    }
}
