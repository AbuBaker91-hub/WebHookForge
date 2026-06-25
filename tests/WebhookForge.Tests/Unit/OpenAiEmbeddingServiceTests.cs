using System.Net;
using Microsoft.Extensions.Options;
using WebhookForge.Application.Common.Settings;
using WebhookForge.Infrastructure.Services;
using Xunit;

namespace WebhookForge.Tests.Unit;

/// <summary>
/// Request-shape + response-parsing tests for the OpenAI embedding client.
/// No network: a capturing handler returns canned JSON. Verifies the key travels in the
/// Authorization header (not the URL), the batch input is sent, vectors are returned in
/// input order regardless of API ordering, and a missing key fails loudly.
/// </summary>
public class OpenAiEmbeddingServiceTests
{
    private const string Key = "sk-secret-embedding-key";

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;
        public string? LastBody;
        private readonly string _responseJson;
        public CapturingHandler(string responseJson) => _responseJson = responseJson;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            if (request.Content is not null) LastBody = await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_responseJson) };
        }
    }

    private static OpenAiEmbeddingService Build(CapturingHandler handler, string? key = Key) =>
        new(new HttpClient(handler), Options.Create(new RagSettings
        {
            EmbeddingApiKey     = key,
            EmbeddingModel      = "text-embedding-3-small",
            EmbeddingDimensions = 3
        }));

    [Fact] // TC-EMB-01 — key in Authorization: Bearer header, never the URL
    public async Task Embed_SendsKeyInAuthorizationHeader_NotInUrl()
    {
        var handler = new CapturingHandler("""{"data":[{"index":0,"embedding":[0.1,0.2,0.3]}]}""");
        var svc = Build(handler);

        _ = await svc.EmbedAsync("hello");

        Assert.Equal("https://api.openai.com/v1/embeddings", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal(Key, handler.LastRequest!.Headers.Authorization!.Parameter);
        Assert.DoesNotContain(Key, handler.LastRequest!.RequestUri!.ToString());
        Assert.Contains("text-embedding-3-small", handler.LastBody);
    }

    [Fact] // TC-EMB-02 — batch results are returned in INPUT order even if the API reorders them
    public async Task EmbedBatch_ReordersResultsByIndex()
    {
        // API returns index 1 before index 0 on purpose.
        var handler = new CapturingHandler(
            """{"data":[{"index":1,"embedding":[0.4,0.5,0.6]},{"index":0,"embedding":[0.1,0.2,0.3]}]}""");
        var svc = Build(handler);

        var vectors = await svc.EmbedBatchAsync(new[] { "first", "second" });

        Assert.Equal(2, vectors.Count);
        Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, vectors[0]); // input[0] → index 0
        Assert.Equal(new[] { 0.4f, 0.5f, 0.6f }, vectors[1]); // input[1] → index 1
    }

    [Fact] // TC-EMB-03 — no key configured fails loudly rather than calling the API
    public async Task Embed_NoKey_Throws()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
        var handler = new CapturingHandler("{}");
        var svc = Build(handler, key: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.EmbedAsync("x"));
        Assert.Null(handler.LastRequest); // never hit the wire
    }

    [Fact] // TC-EMB-04 — empty batch short-circuits with no API call
    public async Task EmbedBatch_Empty_ReturnsEmptyNoCall()
    {
        var handler = new CapturingHandler("{}");
        var svc = Build(handler);

        var vectors = await svc.EmbedBatchAsync(Array.Empty<string>());

        Assert.Empty(vectors);
        Assert.Null(handler.LastRequest);
    }
}
