using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace WebhookForge.Tests.Infrastructure;

/// <summary>Lightweight typed views over the JSON the API returns (camelCase by default).</summary>
public record AuthResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt, UserInfo User);
public record UserInfo(Guid Id, string Email, string DisplayName, string? AiProvider);
public record WorkspaceResponse(Guid Id, string Name, string Slug, Guid OwnerId);
public record EndpointResponse(Guid Id, Guid WorkspaceId, string Token, string Name, bool IsActive, string WebhookUrl);
public record MockRuleResponse(Guid Id, Guid EndpointId, string Name, int Priority, short ResponseStatus, string? ResponseBody, bool IsActive);
public record RequestListResponse(List<IncomingRequestView> Items, int TotalCount, int Page, int PageSize);
public record IncomingRequestView(Guid Id, string Method, string? Body, string? IpAddress);

/// <summary>Shared HTTP helpers so tests read like API calls, not plumbing.</summary>
public static class ApiClientExtensions
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Register a fresh user and return the parsed auth response.</summary>
    public static async Task<AuthResponse> RegisterAsync(
        this HttpClient client, string? email = null, string password = "Sup3rSecret!", string display = "Test User")
    {
        email ??= $"user-{Guid.NewGuid():N}@example.com";
        var res = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password, displayName = display });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
    }

    /// <summary>Register a user and set the Bearer token on the client for subsequent calls.</summary>
    public static async Task<AuthResponse> RegisterAndAuthenticateAsync(
        this HttpClient client, string? email = null, string password = "Sup3rSecret!")
    {
        var auth = await client.RegisterAsync(email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return auth;
    }

    public static async Task<WorkspaceResponse> CreateWorkspaceAsync(this HttpClient client, string name = "WS")
    {
        var res = await client.PostAsJsonAsync("/api/workspaces", new { name });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<WorkspaceResponse>(Json))!;
    }

    public static async Task<EndpointResponse> CreateEndpointAsync(this HttpClient client, Guid workspaceId, string name = "EP")
    {
        var res = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/endpoints", new { name });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<EndpointResponse>(Json))!;
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage res)
        => (await res.Content.ReadFromJsonAsync<T>(Json))!;
}
