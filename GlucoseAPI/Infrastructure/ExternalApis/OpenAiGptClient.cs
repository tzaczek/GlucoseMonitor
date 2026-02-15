using System.Text;
using System.Text.Json;
using GlucoseAPI.Application.Interfaces;

namespace GlucoseAPI.Infrastructure.ExternalApis;

/// <summary>
/// Infrastructure implementation of <see cref="IGptClient"/> using IHttpClientFactory.
/// Handles HTTP communication with the OpenAI API.
/// </summary>
public class OpenAiGptClient : IGptClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenAiGptClient> _logger;

    public OpenAiGptClient(IHttpClientFactory httpClientFactory, ILogger<OpenAiGptClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<GptAnalysisResult> AnalyzeAsync(
        string apiKey,
        string systemPrompt,
        string userPrompt,
        string modelName = "gpt-4o-mini",
        int maxTokens = 4096,
        CancellationToken ct = default)
    {
        var httpClient = _httpClientFactory.CreateClient("OpenAI");
        httpClient.DefaultRequestHeaders.Clear();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        var requestBody = new
        {
            model = modelName,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            max_completion_tokens = maxTokens
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogDebug("Calling OpenAI API with model '{Model}'...", modelName);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync("v1/chat/completions", content, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "HTTP request to OpenAI API failed.");
            return GptAnalysisResult.Failure(modelName, 0, (int)sw.ElapsedMilliseconds, ex.Message);
        }
        sw.Stop();

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("OpenAI API returned {Status}: {Body}", response.StatusCode, errorBody);
            return GptAnalysisResult.Failure(
                modelName, (int)response.StatusCode, (int)sw.ElapsedMilliseconds, errorBody);
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var gptResponse = JsonSerializer.Deserialize<GptChatResponse>(responseJson);

        var usage = gptResponse?.Usage;
        var finishReason = gptResponse?.Choices?.FirstOrDefault()?.FinishReason;
        var resultContent = gptResponse?.Choices?.FirstOrDefault()?.Message?.Content;

        if (string.IsNullOrWhiteSpace(resultContent))
        {
            _logger.LogWarning("OpenAI response content is empty. Finish reason: {Reason}", finishReason ?? "unknown");
        }

        return new GptAnalysisResult(
            Content: resultContent,
            Model: gptResponse?.Model ?? modelName,
            InputTokens: usage?.PromptTokens ?? 0,
            OutputTokens: usage?.CompletionTokens ?? 0,
            TotalTokens: usage?.TotalTokens ?? 0,
            FinishReason: finishReason,
            HttpStatusCode: (int)response.StatusCode,
            Success: true,
            DurationMs: (int)sw.ElapsedMilliseconds);
    }
}
