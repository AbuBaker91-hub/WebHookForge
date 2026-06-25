using System.Text;
using WebhookForge.Domain.Entities;

namespace WebhookForge.Infrastructure.Rag;

/// <summary>
/// Turns a captured request into one or more overlapping text chunks ready for embedding.
///
/// Webhook payloads are usually small (a single chunk), but large bodies are split with
/// a sliding window so no single embedding has to represent too much, and so context that
/// straddles a boundary still appears in at least one chunk (that's what the overlap is for).
/// </summary>
public static class TextChunker
{
    /// <summary>Flatten a request into a single embeddable document (verb, path, type, headers, body).</summary>
    public static string BuildDocument(IncomingRequest r)
    {
        var sb = new StringBuilder();
        sb.Append(r.Method).Append(' ').AppendLine(r.Path ?? "/");
        if (!string.IsNullOrEmpty(r.ContentType)) sb.Append("Content-Type: ").AppendLine(r.ContentType);
        if (!string.IsNullOrEmpty(r.QueryString)) sb.Append("Query: ").AppendLine(r.QueryString);
        if (!string.IsNullOrEmpty(r.Headers))     sb.Append("Headers: ").AppendLine(r.Headers);
        if (!string.IsNullOrEmpty(r.Body))        sb.Append("Body: ").AppendLine(r.Body);
        return sb.ToString();
    }

    /// <summary>Split text into overlapping windows. Returns at least one chunk for non-empty input.</summary>
    public static List<string> Split(string text, int chunkSize, int overlap)
    {
        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return chunks;
        if (chunkSize <= 0) chunkSize = 1800;
        if (overlap < 0 || overlap >= chunkSize) overlap = Math.Min(200, chunkSize / 4);

        var stride = chunkSize - overlap;
        for (var start = 0; start < text.Length; start += stride)
        {
            var length = Math.Min(chunkSize, text.Length - start);
            chunks.Add(text.Substring(start, length));
            if (start + length >= text.Length) break;
        }
        return chunks;
    }
}
