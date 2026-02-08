using System.Text.Json.Serialization;

namespace GlucoseAPI.Infrastructure.ExternalApis;

/// <summary>
/// Shared GPT API response models used by OpenAiGptClient.
/// </summary>
internal class GptChatResponse
{
    [JsonPropertyName("choices")]
    public List<GptChoice>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public GptUsage? Usage { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }
}

internal class GptChoice
{
    [JsonPropertyName("message")]
    public GptMessage? Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

internal class GptMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

internal class GptUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}
