using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WebhookForge.Tests.Infrastructure;
using Xunit;

namespace WebhookForge.Tests.Integration;

public class AiAnalysisApiTests : IClassFixture<WebhookForgeApiFactory>
{
    private readonly WebhookForgeApiFactory _factory;
    public AiAnalysisApiTests(WebhookForgeApiFactory factory) => _factory = factory;

    private const string PlaintextKey = "sk-test-PLAINTEXT-should-never-be-stored-123456";

    [Fact] // TC-AI-01 — saving an AI key persists ciphertext, never the plaintext
    public async Task SaveAiSettings_StoresEncryptedKey_NotPlaintext()
    {
        var client = _factory.CreateClient();
        var auth = await client.RegisterAndAuthenticateAsync();

        var save = await client.PutAsJsonAsync("/api/auth/me/ai-settings",
            new { provider = "Gemini", apiKey = PlaintextKey });
        Assert.Equal(HttpStatusCode.NoContent, save.StatusCode);

        var storedKey = await _factory.WithDbAsync(async db =>
            (await db.Users.SingleAsync(u => u.Email == auth.User.Email)).AiApiKey);

        Assert.False(string.IsNullOrEmpty(storedKey));
        Assert.NotEqual(PlaintextKey, storedKey);          // encrypted at rest
        Assert.DoesNotContain(PlaintextKey, storedKey!);   // plaintext not embedded anywhere
    }

    [Fact] // TC-AI-02 — full pipeline: auth → decrypt key → fetch request → call provider → return summary
    public async Task Analyze_WithConfiguredProvider_ReturnsAnalysis()
    {
        var client = _factory.CreateClient();
        await client.RegisterAndAuthenticateAsync();
        await client.PutAsJsonAsync("/api/auth/me/ai-settings", new { provider = "Gemini", apiKey = PlaintextKey });

        var ws = await client.CreateWorkspaceAsync();
        var ep = await client.CreateEndpointAsync(ws.Id);

        var anon = _factory.CreateClient();
        await anon.PostAsync($"/hooks/{ep.Token}",
            new StringContent("""{"event":"payment.succeeded"}""", Encoding.UTF8, "application/json"));

        var list = await (await client.GetAsync($"/api/endpoints/{ep.Id}/requests")).ReadAsync<RequestListResponse>();
        var requestId = list.Items[0].Id;

        var res = await client.PostAsync($"/api/requests/{requestId}/analyze", null);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains(StubAiAnalysisService.Marker, body); // decrypt succeeded and provider was invoked
        Assert.Contains("Gemini", body);
    }

    [Fact] // TC-AI-03 — analyze without a configured provider is rejected
    public async Task Analyze_WithoutProvider_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        await client.RegisterAndAuthenticateAsync();
        var ws = await client.CreateWorkspaceAsync();
        var ep = await client.CreateEndpointAsync(ws.Id);

        var anon = _factory.CreateClient();
        await anon.PostAsync($"/hooks/{ep.Token}", new StringContent("{}", Encoding.UTF8, "application/json"));
        var list = await (await client.GetAsync($"/api/endpoints/{ep.Id}/requests")).ReadAsync<RequestListResponse>();

        var res = await client.PostAsync($"/api/requests/{list.Items[0].Id}/analyze", null);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact] // TC-AI-04 — the API key is never returned to the client
    public async Task Profile_NeverExposesApiKey()
    {
        var client = _factory.CreateClient();
        await client.RegisterAndAuthenticateAsync();
        await client.PutAsJsonAsync("/api/auth/me/ai-settings", new { provider = "Gemini", apiKey = PlaintextKey });

        var me = await client.GetAsync("/api/auth/me");
        var body = await me.Content.ReadAsStringAsync();

        Assert.DoesNotContain(PlaintextKey, body);
        Assert.Contains("Gemini", body); // provider name is fine to expose
    }
}
