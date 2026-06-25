using WebhookForge.Domain.Enums;

namespace WebhookForge.Application.Common.Interfaces;

/// <summary>
/// AI analysis service supporting multiple providers (Claude, Gemini, Groq).
/// Routes to the correct provider based on the user's saved choice.
/// </summary>
public interface IAiAnalysisService
{
    /// <summary>
    /// Analyzes an incoming webhook request using the specified AI provider and key.
    /// </summary>
    Task<string> AnalyzeWebhookAsync(
        AiProvider provider,
        string     apiKey,
        string     method,
        string?    path,
        string?    headers,
        string?    body,
        CancellationToken ct = default);

    /// <summary>
    /// Runs an arbitrary prompt through the selected provider and returns the completion text.
    /// Used by the RAG pipeline to generate grounded answers from retrieved context.
    /// </summary>
    Task<string> CompleteAsync(
        AiProvider provider,
        string     apiKey,
        string     prompt,
        int        maxTokens = 800,
        CancellationToken ct = default);
}
