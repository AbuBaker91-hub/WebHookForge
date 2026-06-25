using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WebhookForge.Application.Common.Interfaces;
using WebhookForge.Application.Common.Settings;

namespace WebhookForge.Infrastructure.Services;

/// <summary>
/// Embeds text via OpenAI's embeddings API (default model: text-embedding-3-small, 1536 dims).
/// The API key is read from configuration (Rag:EmbeddingApiKey) or the OPENAI_API_KEY env var.
/// Uses a pooled, typed HttpClient.
/// </summary>
public class OpenAiEmbeddingService : IEmbeddingService
{
    private const string EmbeddingsEndpoint = "https://api.openai.com/v1/embeddings";

    private readonly HttpClient  _http;
    private readonly RagSettings _settings;
    private readonly string      _apiKey;

    public OpenAiEmbeddingService(HttpClient http, IOptions<RagSettings> settings)
    {
        _http     = http;
        _settings = settings.Value;
        _apiKey   = _settings.EmbeddingApiKey
                    ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                    ?? string.Empty;
    }

    public int Dimensions => _settings.EmbeddingDimensions;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        => (await EmbedBatchAsync(new[] { text }, ct))[0];

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
            throw new InvalidOperationException(
                "No embedding API key configured. Set Rag:EmbeddingApiKey or the OPENAI_API_KEY environment variable.");
        if (texts.Count == 0) return Array.Empty<float[]>();

        using var request = new HttpRequestMessage(HttpMethod.Post, EmbeddingsEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { model = _settings.EmbeddingModel, input = texts }),
            Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        // Response shape: { "data": [ { "index": 0, "embedding": [..] }, ... ] }
        // Sort by "index" so the result order matches the input order regardless of API ordering.
        var ordered = doc.RootElement.GetProperty("data").EnumerateArray()
            .OrderBy(e => e.GetProperty("index").GetInt32())
            .Select(e =>
            {
                var arr = e.GetProperty("embedding");
                var vec = new float[arr.GetArrayLength()];
                var i = 0;
                foreach (var f in arr.EnumerateArray()) vec[i++] = f.GetSingle();
                return vec;
            })
            .ToList();

        return ordered;
    }
}
