namespace WebhookForge.Application.DTOs.Rag;

/// <summary>Request body for a RAG question over an endpoint's captured webhooks.</summary>
public class RagAskDto
{
    /// <summary>The natural-language question to answer from the indexed webhook history.</summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>How many of the most-similar chunks to feed the model as context (1–20).</summary>
    public int TopK { get; set; } = 5;
}

/// <summary>A grounded answer plus the source chunks it was built from.</summary>
public class RagAnswerDto
{
    public string Answer { get; set; } = string.Empty;

    /// <summary>The retrieved chunks that grounded the answer, best match first.</summary>
    public List<RagCitationDto> Citations { get; set; } = new();

    /// <summary>Number of chunks retrieved (0 means nothing indexed yet — ingest first).</summary>
    public int ChunksRetrieved { get; set; }
}

/// <summary>One retrieved chunk, traceable back to the original captured request.</summary>
public class RagCitationDto
{
    public Guid     RequestId  { get; set; }
    public string   Method     { get; set; } = string.Empty;
    public string?  Path       { get; set; }
    public DateTime ReceivedAt { get; set; }

    /// <summary>Cosine similarity in [0,1] — higher is closer.</summary>
    public double   Score      { get; set; }

    /// <summary>A short preview of the matched chunk text.</summary>
    public string   Snippet    { get; set; } = string.Empty;
}

/// <summary>Outcome of (re)building the vector index for an endpoint.</summary>
public class RagIngestResultDto
{
    public int RequestsProcessed { get; set; }
    public int ChunksIndexed     { get; set; }
}
