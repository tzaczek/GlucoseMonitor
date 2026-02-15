namespace GlucoseAPI.Application.Interfaces;

/// <summary>
/// Port interface for GPT API communication.
/// Abstracts the HTTP call to OpenAI so services don't depend on HttpClient directly.
/// </summary>
public interface IGptClient
{
    /// <summary>
    /// Send a chat completion request to the GPT API.
    /// </summary>
    /// <param name="apiKey">The API key for authentication.</param>
    /// <param name="systemPrompt">The system prompt.</param>
    /// <param name="userPrompt">The user prompt.</param>
    /// <param name="modelName">The model to use (e.g. "gpt-4o-mini").</param>
    /// <param name="maxTokens">Maximum completion tokens.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the API call, including content, usage, and status.</returns>
    Task<GptAnalysisResult> AnalyzeAsync(
        string apiKey,
        string systemPrompt,
        string userPrompt,
        string modelName = "gpt-4o-mini",
        int maxTokens = 4096,
        CancellationToken ct = default);
}

/// <summary>
/// Immutable result object from a GPT API call.
/// </summary>
public record GptAnalysisResult(
    string? Content,
    string Model,
    int InputTokens,
    int OutputTokens,
    int TotalTokens,
    string? FinishReason,
    int HttpStatusCode,
    bool Success,
    int DurationMs,
    string? ErrorMessage = null)
{
    /// <summary>
    /// Create a failed result.
    /// </summary>
    public static GptAnalysisResult Failure(string model, int httpStatusCode, int durationMs, string? errorMessage)
        => new(null, model, 0, 0, 0, null, httpStatusCode, false, durationMs, errorMessage);
}
