using System.Net;
using System.Net.Http.Json;
using System.Text;
using WebhookForge.Tests.Infrastructure;
using Xunit;

namespace WebhookForge.Tests.Integration;

public class WebhookCaptureApiTests : IClassFixture<WebhookForgeApiFactory>
{
    private readonly WebhookForgeApiFactory _factory;
    public WebhookCaptureApiTests(WebhookForgeApiFactory factory) => _factory = factory;

    private async Task<(HttpClient auth, EndpointResponse ep)> SetupEndpointAsync()
    {
        var client = _factory.CreateClient();
        await client.RegisterAndAuthenticateAsync();
        var ws = await client.CreateWorkspaceAsync();
        var ep = await client.CreateEndpointAsync(ws.Id);
        return (client, ep);
    }

    [Fact] // TC-HOOK-01 — capture default response and persist the request
    public async Task PostToHook_CapturesRequest_AndIsListable()
    {
        var (auth, ep) = await SetupEndpointAsync();
        var anon = _factory.CreateClient();

        var hook = await anon.PostAsync($"/hooks/{ep.Token}",
            new StringContent("""{"event":"order.created"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, hook.StatusCode);
        Assert.Contains("received", await hook.Content.ReadAsStringAsync());

        var list = await (await auth.GetAsync($"/api/endpoints/{ep.Id}/requests")).ReadAsync<RequestListResponse>();
        Assert.Equal(1, list.TotalCount);
        Assert.Equal("POST", list.Items[0].Method);
        Assert.Contains("order.created", list.Items[0].Body);
    }

    [Fact] // TC-HOOK-02
    public async Task PostToUnknownToken_ReturnsNotFound()
    {
        var anon = _factory.CreateClient();
        var res = await anon.PostAsync("/hooks/does-not-exist",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact] // TC-HOOK-03 — a matching mock rule overrides the default response
    public async Task MatchingMockRule_ReturnsCustomStatusAndBody()
    {
        var (auth, ep) = await SetupEndpointAsync();

        var create = await auth.PostAsJsonAsync($"/api/endpoints/{ep.Id}/rules", new
        {
            name = "Created",
            priority = 10,
            matchMethod = "POST",
            responseStatus = 201,
            responseBody = """{"status":"created"}""",
            delayMs = 0
        });
        create.EnsureSuccessStatusCode();

        var anon = _factory.CreateClient();
        var hook = await anon.PostAsync($"/hooks/{ep.Token}",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Created, hook.StatusCode);
        Assert.Contains("created", await hook.Content.ReadAsStringAsync());
    }

    [Fact] // TC-HOOK-04 — lowest priority number wins when multiple rules match
    public async Task MockRulePriority_FirstMatchWins()
    {
        var (auth, ep) = await SetupEndpointAsync();

        await auth.PostAsJsonAsync($"/api/endpoints/{ep.Id}/rules",
            new { name = "Low", priority = 20, matchMethod = "POST", responseStatus = 202, responseBody = "low" });
        await auth.PostAsJsonAsync($"/api/endpoints/{ep.Id}/rules",
            new { name = "High", priority = 10, matchMethod = "POST", responseStatus = 201, responseBody = "high" });

        var anon = _factory.CreateClient();
        var hook = await anon.PostAsync($"/hooks/{ep.Token}",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Created, hook.StatusCode); // 201 from priority-10 rule
        Assert.Contains("high", await hook.Content.ReadAsStringAsync());
    }
}
