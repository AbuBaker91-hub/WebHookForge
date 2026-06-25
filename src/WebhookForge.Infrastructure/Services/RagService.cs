using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using WebhookForge.Application.Common.Helpers;
using WebhookForge.Application.Common.Interfaces;
using WebhookForge.Application.Common.Models;
using WebhookForge.Application.Common.Settings;
using WebhookForge.Application.DTOs.Rag;
using WebhookForge.Domain.Enums;
using WebhookForge.Infrastructure.Data;
using WebhookForge.Infrastructure.Rag;

namespace WebhookForge.Infrastructure.Services;

/// <summary>
/// Retrieval-augmented generation over an endpoint's captured webhooks.
/// Ingestion reads from the SQL Server store, embeds chunks, and writes vectors to pgvector.
/// Asking embeds the question, retrieves the nearest chunks (cosine distance), and has the
/// caller's chosen LLM answer grounded strictly in that retrieved context.
/// </summary>
public class RagService : IRagService
{
    private const int MaxRequestsPerIngest = 1000;  // bound embedding cost on huge endpoints
    private const int EmbedBatchSize        = 64;    // chunks per embeddings API round-trip

    private readonly ApplicationDbContext _app;
    private readonly RagDbContext         _rag;
    private readonly IEmbeddingService    _embeddings;
    private readonly IAiAnalysisService   _ai;
    private readonly IUnitOfWork          _uow;
    private readonly RagSettings          _settings;

    public RagService(
        ApplicationDbContext app,
        RagDbContext         rag,
        IEmbeddingService    embeddings,
        IAiAnalysisService   ai,
        IUnitOfWork          uow,
        IOptions<RagSettings> settings)
    {
        _app        = app;
        _rag        = rag;
        _embeddings = embeddings;
        _ai         = ai;
        _uow        = uow;
        _settings   = settings.Value;
    }

    /// <inheritdoc/>
    public async Task<Result<RagIngestResultDto>> IngestEndpointAsync(
        Guid endpointId, Guid userId, CancellationToken ct = default)
    {
        var access = await AccessGuard.RequireEndpointAccessAsync(_uow, endpointId, userId, ct);
        if (!access.IsSuccess) return Result<RagIngestResultDto>.Forbidden(access.Error!);

        var requests = await _app.IncomingRequests
            .AsNoTracking()
            .Where(r => r.EndpointId == endpointId)
            .OrderByDescending(r => r.ReceivedAt)
            .Take(MaxRequestsPerIngest)
            .ToListAsync(ct);

        // Full rebuild for this endpoint — idempotent and safe to re-run.
        await _rag.Chunks.Where(c => c.EndpointId == endpointId).ExecuteDeleteAsync(ct);

        // Flatten every request into overlapping chunks, remembering which request each came from.
        var pending = new List<RequestChunk>();
        foreach (var r in requests)
        {
            var doc = TextChunker.BuildDocument(r);
            foreach (var piece in TextChunker.Split(doc, _settings.ChunkSize, _settings.ChunkOverlap))
            {
                pending.Add(new RequestChunk
                {
                    EndpointId        = endpointId,
                    IncomingRequestId = r.Id,
                    Content           = piece,
                    Method            = r.Method,
                    Path              = r.Path,
                    ReceivedAt        = r.ReceivedAt
                });
            }
        }

        // Embed in batches and attach vectors.
        for (var i = 0; i < pending.Count; i += EmbedBatchSize)
        {
            var batch   = pending.GetRange(i, Math.Min(EmbedBatchSize, pending.Count - i));
            var vectors = await _embeddings.EmbedBatchAsync(batch.Select(c => c.Content).ToList(), ct);
            for (var j = 0; j < batch.Count; j++)
                batch[j].Embedding = new Vector(vectors[j]);
        }

        if (pending.Count > 0)
        {
            await _rag.Chunks.AddRangeAsync(pending, ct);
            await _rag.SaveChangesAsync(ct);
        }

        return Result<RagIngestResultDto>.Success(new RagIngestResultDto
        {
            RequestsProcessed = requests.Count,
            ChunksIndexed     = pending.Count
        });
    }

    /// <inheritdoc/>
    public async Task<Result<RagAnswerDto>> AskAsync(
        Guid endpointId, Guid userId, AiProvider provider, string apiKey,
        RagAskDto request, CancellationToken ct = default)
    {
        var access = await AccessGuard.RequireEndpointAccessAsync(_uow, endpointId, userId, ct);
        if (!access.IsSuccess) return Result<RagAnswerDto>.Forbidden(access.Error!);

        if (string.IsNullOrWhiteSpace(request.Question))
            return Result<RagAnswerDto>.Failure("Question is required.");

        var topK = request.TopK is > 0 and <= 20 ? request.TopK : _settings.DefaultTopK;

        // 1. Embed the question.
        var queryVector = new Vector(await _embeddings.EmbedAsync(request.Question, ct));

        // 2. Retrieve the nearest chunks by cosine distance (smaller = closer).
        var hits = await _rag.Chunks
            .Where(c => c.EndpointId == endpointId)
            .Select(c => new { Chunk = c, Distance = c.Embedding.CosineDistance(queryVector) })
            .OrderBy(x => x.Distance)
            .Take(topK)
            .ToListAsync(ct);

        if (hits.Count == 0)
            return Result<RagAnswerDto>.Success(new RagAnswerDto
            {
                Answer          = "No webhooks have been indexed for this endpoint yet. Run \"Re-index\" first, then ask again.",
                ChunksRetrieved = 0
            });

        // 3. Build grounded context and ask the LLM.
        var prompt = BuildRagPrompt(request.Question, hits.Select(h => h.Chunk).ToList());
        var answer = await _ai.CompleteAsync(provider, apiKey, prompt, 800, ct);

        // 4. Map citations (cosine similarity = 1 - distance, clamped to [0,1]).
        var citations = hits.Select(h => new RagCitationDto
        {
            RequestId  = h.Chunk.IncomingRequestId,
            Method     = h.Chunk.Method,
            Path       = h.Chunk.Path,
            ReceivedAt = h.Chunk.ReceivedAt,
            Score      = Math.Clamp(1.0 - h.Distance, 0.0, 1.0),
            Snippet    = h.Chunk.Content.Length > 200 ? h.Chunk.Content[..200] + "…" : h.Chunk.Content
        }).ToList();

        return Result<RagAnswerDto>.Success(new RagAnswerDto
        {
            Answer          = answer,
            Citations       = citations,
            ChunksRetrieved = hits.Count
        });
    }

    private static string BuildRagPrompt(string question, List<RequestChunk> chunks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a webhook analysis assistant. Answer the question using ONLY the captured webhook context below.");
        sb.AppendLine("If the context does not contain enough information, say so plainly — do not invent details.");
        sb.AppendLine("Be concise (3–6 sentences). Cite supporting sources inline as [1], [2], etc.");
        sb.AppendLine();
        sb.AppendLine("=== Context ===");
        for (var i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            sb.AppendLine($"[{i + 1}] {c.Method} {c.Path ?? "/"} (received {c.ReceivedAt:u})");
            sb.AppendLine(c.Content);
            sb.AppendLine();
        }
        sb.AppendLine("=== Question ===");
        sb.AppendLine(question);
        return sb.ToString();
    }
}
