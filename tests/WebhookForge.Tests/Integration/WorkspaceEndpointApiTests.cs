using System.Net;
using System.Net.Http.Json;
using WebhookForge.Tests.Infrastructure;
using Xunit;

namespace WebhookForge.Tests.Integration;

public class WorkspaceEndpointApiTests : IClassFixture<WebhookForgeApiFactory>
{
    private readonly WebhookForgeApiFactory _factory;
    public WorkspaceEndpointApiTests(WebhookForgeApiFactory factory) => _factory = factory;

    [Fact] // TC-WS-01
    public async Task CreateWorkspace_ThenListAndGet()
    {
        var client = _factory.CreateClient();
        await client.RegisterAndAuthenticateAsync();

        var ws = await client.CreateWorkspaceAsync("My Space");
        Assert.Equal("My Space", ws.Name);

        var list = await (await client.GetAsync("/api/workspaces")).ReadAsync<List<WorkspaceResponse>>();
        Assert.Contains(list, w => w.Id == ws.Id);

        var get = await client.GetAsync($"/api/workspaces/{ws.Id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }

    [Fact] // TC-WS-02
    public async Task CreateEndpoint_ReturnsTokenAndWebhookUrl()
    {
        var client = _factory.CreateClient();
        await client.RegisterAndAuthenticateAsync();
        var ws = await client.CreateWorkspaceAsync();

        var ep = await client.CreateEndpointAsync(ws.Id, "Orders");

        Assert.False(string.IsNullOrWhiteSpace(ep.Token));
        Assert.Contains(ep.Token, ep.WebhookUrl);
    }

    [Fact] // TC-WS-03 — access control enforced in the service layer
    public async Task OtherUser_CannotAccessForeignWorkspace()
    {
        var owner = _factory.CreateClient();
        await owner.RegisterAndAuthenticateAsync();
        var ws = await owner.CreateWorkspaceAsync("Private");

        var intruder = _factory.CreateClient();
        await intruder.RegisterAndAuthenticateAsync();

        var res = await intruder.GetAsync($"/api/workspaces/{ws.Id}");
        Assert.True(res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"Expected 403/404 but got {(int)res.StatusCode}");
    }

    [Fact] // TC-WS-04
    public async Task RegenerateToken_ChangesTheToken()
    {
        var client = _factory.CreateClient();
        await client.RegisterAndAuthenticateAsync();
        var ws = await client.CreateWorkspaceAsync();
        var ep = await client.CreateEndpointAsync(ws.Id);

        var res = await client.PostAsync($"/api/endpoints/{ep.Id}/regenerate-token", null);
        res.EnsureSuccessStatusCode();
        var updated = await res.ReadAsync<EndpointResponse>();

        Assert.NotEqual(ep.Token, updated.Token);
    }
}
