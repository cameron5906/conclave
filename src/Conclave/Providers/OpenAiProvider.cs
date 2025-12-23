using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Conclave.Abstractions;
using Conclave.Models;
using Conclave.Tools;
using Microsoft.Extensions.Logging;

namespace Conclave.Providers;

public class OpenAiProvider : BaseLlmProvider
{
    private readonly OpenAiOptions _options;

    public override string ProviderId => "openai";
    public override string DisplayName => "OpenAI";

    public OpenAiProvider(HttpClient httpClient, OpenAiOptions options, ILogger<OpenAiProvider>? logger = null)
        : base(httpClient, logger)
    {
        _options = options;
        httpClient.BaseAddress = new Uri(options.BaseUrl ?? "https://api.openai.com/v1/");
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {options.ApiKey}");
        if (!string.IsNullOrEmpty(options.Organization))
        {
            httpClient.DefaultRequestHeaders.Add("OpenAI-Organization", options.Organization);
        }
    }

    public override async Task<LlmResponse> CompleteAsync(
        IReadOnlyList<Message> messages,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(messages, options);
        var response = await SendRequestAsync<OpenAiChatResponse>("chat/completions", request, cancellationToken);
        return MapResponse(response);
    }

    public override async Task<LlmResponse> CompleteWithToolsAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition> tools,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(messages, options);
        request.Tools = tools.Select(t => new OpenAiTool
        {
            Type = "function",
            Function = new OpenAiFunction
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = t.Parameters
            }
        }).ToList();

        var response = await SendRequestAsync<OpenAiChatResponse>("chat/completions", request, cancellationToken);
        return MapResponse(response);
    }

    public override async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<Message> messages,
        LlmCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(messages, options);
        request.Stream = true;

        await foreach (var chunk in StreamRequestAsync("chat/completions", request, ExtractStreamContent, cancellationToken))
        {
            yield return chunk;
        }
    }

    private OpenAiChatRequest BuildRequest(IReadOnlyList<Message> messages, LlmCompletionOptions? options)
    {
        var openAiMessages = new List<OpenAiMessage>();

        if (!string.IsNullOrEmpty(options?.SystemPrompt))
        {
            openAiMessages.Add(new OpenAiMessage { Role = "system", Content = options.SystemPrompt });
        }

        foreach (var msg in messages)
        {
            openAiMessages.Add(new OpenAiMessage
            {
                Role = msg.Role switch
                {
                    MessageRole.System => "system",
                    MessageRole.User => "user",
                    MessageRole.Assistant => "assistant",
                    MessageRole.Tool => "tool",
                    _ => "user"
                },
                Content = msg.Content,
                Name = msg.Name,
                ToolCallId = msg.ToolCallId,
                ToolCalls = msg.ToolCalls?.Select(tc => new OpenAiToolCall
                {
                    Id = tc.Id,
                    Type = "function",
                    Function = new OpenAiFunctionCall { Name = tc.Name, Arguments = tc.Arguments }
                }).ToList()
            });
        }

        return new OpenAiChatRequest
        {
            Model = options?.Model ?? _options.DefaultModel ?? "gpt-4o",
            Messages = openAiMessages,
            Temperature = options?.Temperature ?? _options.DefaultTemperature,
            MaxTokens = options?.MaxTokens ?? _options.DefaultMaxTokens,
            TopP = options?.TopP,
            FrequencyPenalty = options?.FrequencyPenalty,
            PresencePenalty = options?.PresencePenalty,
            Stop = options?.StopSequences?.ToList()
        };
    }

    private LlmResponse MapResponse(OpenAiChatResponse response)
    {
        var choice = response.Choices.FirstOrDefault();
        return new LlmResponse
        {
            Content = choice?.Message?.Content ?? string.Empty,
            ToolCalls = choice?.Message?.ToolCalls?.Select(tc => new ToolCall
            {
                Id = tc.Id,
                Name = tc.Function.Name,
                Arguments = tc.Function.Arguments
            }).ToList(),
            Usage = response.Usage != null ? new CompletionUsage
            {
                PromptTokens = response.Usage.PromptTokens,
                CompletionTokens = response.Usage.CompletionTokens
            } : null,
            FinishReason = choice?.FinishReason,
            ModelId = response.Model
        };
    }

    private string? ExtractStreamContent(string data)
    {
        try
        {
            var chunk = JsonSerializer.Deserialize<OpenAiStreamChunk>(data, JsonOptions);
            return chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
        }
        catch
        {
            return null;
        }
    }
}

public class OpenAiOptions
{
    public string ApiKey { get; init; } = string.Empty;
    public string? Organization { get; init; }
    public string? BaseUrl { get; init; }
    public string? DefaultModel { get; init; } = "gpt-4o";
    public double? DefaultTemperature { get; init; }
    public int? DefaultMaxTokens { get; init; }
}

internal class OpenAiChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<OpenAiMessage> Messages { get; set; } = new();

    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; set; }

    [JsonPropertyName("max_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; set; }

    [JsonPropertyName("top_p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TopP { get; set; }

    [JsonPropertyName("frequency_penalty")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? FrequencyPenalty { get; set; }

    [JsonPropertyName("presence_penalty")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? PresencePenalty { get; set; }

    [JsonPropertyName("stop")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Stop { get; set; }

    [JsonPropertyName("stream")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Stream { get; set; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OpenAiTool>? Tools { get; set; }
}

internal class OpenAiMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; set; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }

    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OpenAiToolCall>? ToolCalls { get; set; }
}

internal class OpenAiTool
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OpenAiFunction Function { get; set; } = new();
}

internal class OpenAiFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public ToolParameters Parameters { get; set; } = new();
}

internal class OpenAiToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OpenAiFunctionCall Function { get; set; } = new();
}

internal class OpenAiFunctionCall
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = string.Empty;
}

internal class OpenAiChatResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("choices")]
    public List<OpenAiChoice> Choices { get; set; } = new();

    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; set; }
}

internal class OpenAiChoice
{
    [JsonPropertyName("message")]
    public OpenAiMessage? Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

internal class OpenAiUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }
}

internal class OpenAiStreamChunk
{
    [JsonPropertyName("choices")]
    public List<OpenAiStreamChoice>? Choices { get; set; }
}

internal class OpenAiStreamChoice
{
    [JsonPropertyName("delta")]
    public OpenAiDelta? Delta { get; set; }
}

internal class OpenAiDelta
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}
