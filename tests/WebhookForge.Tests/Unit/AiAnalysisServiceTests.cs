using System.Net;
using WebhookForge.Domain.Enums;
using WebhookForge.Infrastructure.Services;
using Xunit;

namespace WebhookForge.Tests.Unit;

/// <summary>
/// Regression for the interviewer's finding: provider API keys must travel in request
/// HEADERS, never in the URL/query string (where they leak into logs).
/// </summary>
public class AiAnalysisServiceTests
{
    private const string Key = "super-secret-provider-key-XYZ";

    /// <summary>Captures the outgoing request and returns a canned provider response.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;
        private readonly string _responseJson;
        public CapturingHandler(string responseJson) => _responseJson = responseJson;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson)
            });
        }
    }

    [Fact] // TC-AISVC-01 — Gemini key passed via x-goog-api-key header, NOT the URL
    public async Task Gemini_SendsKeyInHeader_NotInUrl()
    {
        var handler = new CapturingHandler("""{"candidates":[{"content":{"parts":[{"text":"hello"}]}}]}""");
        var svc = new AiAnalysisService(new HttpClient(handler));

        var result = await svc.AnalyzeWebhookAsync(AiProvider.Gemini, Key, "POST", "/x", null, "{}");

        Assert.Equal("hello", result);
        Assert.True(handler.LastRequest!.Headers.TryGetValues("x-goog-api-key", out var values));
        Assert.Equal(Key, Assert.Single(values!));

        var uri = handler.LastRequest!.RequestUri!.ToString();
        Assert.DoesNotContain(Key, uri);
        Assert.DoesNotContain("key=", uri);
    }

    [Fact] // TC-AISVC-02 — Groq key passed via the Authorization: Bearer header
    public async Task Groq_SendsKeyInAuthorizationHeader()
    {
        var handler = new CapturingHandler("""{"choices":[{"message":{"content":"hi"}}]}""");
        var svc = new AiAnalysisService(new HttpClient(handler));

        var result = await svc.AnalyzeWebhookAsync(AiProvider.Groq, Key, "POST", "/x", null, "{}");

        Assert.Equal("hi", result);
        var authHeader = handler.LastRequest!.Headers.Authorization;
        Assert.NotNull(authHeader);
        Assert.Equal("Bearer", authHeader!.Scheme);
        Assert.Equal(Key, authHeader.Parameter);
        Assert.DoesNotContain(Key, handler.LastRequest!.RequestUri!.ToString());
    }
}
