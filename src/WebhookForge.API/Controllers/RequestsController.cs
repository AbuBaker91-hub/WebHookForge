using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebhookForge.Application.Common.Interfaces;

namespace WebhookForge.API.Controllers;

/// <summary>
/// Read-only access to captured webhook requests.
/// Purge endpoint allows bulk-deleting old requests.
/// All endpoints require authentication.
/// </summary>
[Authorize]
[ApiController]
[Route("api")]
public class RequestsController : BaseController
{
    private readonly IRequestService     _requests;
    private readonly IAuthService        _auth;
    private readonly IAiAnalysisService  _ai;

    public RequestsController(IRequestService requests, IAuthService auth, IAiAnalysisService ai)
    {
        _requests = requests;
        _auth     = auth;
        _ai       = ai;
    }

    /// <summary>Paginated list of captured requests for an endpoint. Newest first.</summary>
    [HttpGet("endpoints/{endpointId:guid}/requests")]
    public async Task<IActionResult> List(
        Guid endpointId,
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct     = default)
        => ToActionResult(await _requests.GetByEndpointAsync(endpointId, CurrentUserId, page, pageSize, ct));

    /// <summary>Get the full detail of a single captured request.</summary>
    [HttpGet("requests/{id:guid}", Name = "GetRequest")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => ToActionResult(await _requests.GetByIdAsync(id, CurrentUserId, ct));

    /// <summary>Delete all requests older than N days for the given endpoint.</summary>
    [HttpDelete("endpoints/{endpointId:guid}/requests")]
    public async Task<IActionResult> Purge(
        Guid endpointId,
        [FromQuery] int olderThanDays = 30,
        CancellationToken ct          = default)
        => ToActionResult(await _requests.PurgeAsync(endpointId, olderThanDays, CurrentUserId, ct));

    /// <summary>Use AI to analyze a captured webhook request and return a plain-English summary.</summary>
    [HttpPost("requests/{id:guid}/analyze")]
    public async Task<IActionResult> Analyze(Guid id, CancellationToken ct)
    {
        var (provider, apiKey) = await _auth.GetAiSettingsAsync(CurrentUserId, ct);
        if (provider is null || string.IsNullOrEmpty(apiKey))
            return BadRequest(new { error = "No AI provider configured. Go to Settings to choose a provider and add your API key." });

        var result = await _requests.GetByIdAsync(id, CurrentUserId, ct);
        if (!result.IsSuccess) return ToActionResult(result);

        var req      = result.Value!;
        var analysis = await _ai.AnalyzeWebhookAsync(provider.Value, apiKey, req.Method, req.Path,
                           System.Text.Json.JsonSerializer.Serialize(req.Headers), req.Body, ct);

        return Ok(new { analysis });
    }
}
