using WebhookForge.Domain.Entities;
using WebhookForge.Infrastructure.Rag;
using Xunit;

namespace WebhookForge.Tests.Unit;

/// <summary>
/// Pure-logic tests for the RAG chunker — no infrastructure required.
/// Verifies the sliding-window split (size/overlap/coverage) and document flattening
/// that feed the embedding pipeline.
/// </summary>
public class TextChunkerTests
{
    [Fact] // TC-CHUNK-01 — empty/whitespace input yields no chunks
    public void Split_EmptyOrWhitespace_ReturnsNoChunks()
    {
        Assert.Empty(TextChunker.Split("",    1800, 200));
        Assert.Empty(TextChunker.Split("   ", 1800, 200));
        Assert.Empty(TextChunker.Split(null!, 1800, 200));
    }

    [Fact] // TC-CHUNK-02 — text shorter than the window is a single, verbatim chunk
    public void Split_ShorterThanWindow_ReturnsSingleChunk()
    {
        const string text = "small payload";
        var chunks = TextChunker.Split(text, 1800, 200);

        Assert.Single(chunks);
        Assert.Equal(text, chunks[0]);
    }

    [Fact] // TC-CHUNK-03 — long text splits into overlapping windows that cover the whole input
    public void Split_LongText_ProducesOverlappingWindowsCoveringEverything()
    {
        var text = new string('a', 500) + new string('b', 500) + new string('c', 500); // 1500 chars
        var chunks = TextChunker.Split(text, 600, 100);

        // stride = 600 - 100 = 500 → starts at 0, 500, 1000 → 3 chunks
        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, c => Assert.True(c.Length <= 600));

        // Every original character index must appear in at least one chunk (full coverage).
        Assert.Equal(text[..600],        chunks[0]);
        Assert.Equal(text[500..1100],    chunks[1]);
        Assert.Equal(text[1000..1500],   chunks[2]);
    }

    [Fact] // TC-CHUNK-04 — adjacent chunks share exactly `overlap` characters
    public void Split_AdjacentChunks_ShareOverlap()
    {
        var text = new string('x', 1000);
        var chunks = TextChunker.Split(text, 400, 100);

        // Tail of chunk[0] equals head of chunk[1] for `overlap` chars.
        var tail = chunks[0][^100..];
        var head = chunks[1][..100];
        Assert.Equal(tail, head);
    }

    [Fact] // TC-CHUNK-05 — a degenerate overlap (>= size) is clamped, not an infinite loop
    public void Split_OverlapTooLarge_IsClampedAndTerminates()
    {
        var text = new string('y', 1000);
        var chunks = TextChunker.Split(text, 300, 999); // overlap >= size → clamp to size/4

        Assert.NotEmpty(chunks);
        Assert.True(chunks.Count < 100, "Chunking must terminate, not loop.");
    }

    [Fact] // TC-CHUNK-06 — BuildDocument includes verb, path, and body so they're embeddable
    public void BuildDocument_IncludesKeyRequestParts()
    {
        var req = new IncomingRequest
        {
            Method      = "POST",
            Path        = "/webhooks/stripe",
            ContentType = "application/json",
            Body        = """{"type":"payment_intent.succeeded"}"""
        };

        var doc = TextChunker.BuildDocument(req);

        Assert.Contains("POST", doc);
        Assert.Contains("/webhooks/stripe", doc);
        Assert.Contains("application/json", doc);
        Assert.Contains("payment_intent.succeeded", doc);
    }
}
