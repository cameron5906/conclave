using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Conclave.Abstractions;
using Conclave.Models;
using Conclave.Tools;
using Microsoft.Extensions.Logging;

namespace Conclave.Providers;

public class AnthropicProvider : BaseLlmProvider
{
    private readonly AnthropicOptions _options;

    public override string ProviderId => "anthropic";
    public override string DisplayName => "Anthropic Claude";

    public AnthropicProvider(HttpClient httpClient, AnthropicOptions options, ILogger<AnthropicProvider>? logger = null)
        : base(httpClient, logger)
    {
        _options = options;
        httpClient.BaseAddress = new Uri(options.BaseUrl ?? "https://api.anthropic.com/v1/");
        httpClient.DefaultRequestHeaders.Add("x-api-key", options.ApiKey);
        httpClient.DefaultRequestHeaders.Add("anthropic-version", options.ApiVersion ?? "2023-06-01");
    }

    public override async Task<LlmResponse> CompleteAsync(
        IReadOnlyList<Message> messages,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(messages, options);
        var response = await SendRequestAsync<AnthropicResponse>("messages", request, cancellationToken);
        return MapResponse(response);
    }

    public override async Task<LlmResponse> CompleteWithToolsAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition> tools,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(messages, options);
        request.Tools = tools.Select(t => new AnthropicTool
        {
            Name = t.Name,
            Description = t.Description,
            InputSchema = new AnthropicInputSchema
            {
                Type = "object",
                Properties = t.Parameters.Properties.ToDictionary(
                    p => p.Key,
                    p => new AnthropicProperty
                    {
                        Type = p.Value.Type,
                        Description = p.Value.Description,
                        Enum = p.Value.Enum
                    }),
                Required = t.Parameters.Required
            }
        }).ToList();

        var response = await SendRequestAsync<AnthropicResponse>("messages", request, cancellationToken);
        return MapResponse(response);
    }

    public override async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<Message> messages,
        LlmCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(messages, options);
        request.Stream = true;

        await foreach (var chunk in StreamRequestAsync("messages", request, ExtractStreamContent, cancellationToken))
        {
            yield return chunk;
        }
    }

    private AnthropicRequest BuildRequest(IReadOnlyList<Message> messages, LlmCompletionOptions? options)
    {
        var anthropicMessages = new List<AnthropicMessage>();
        string? systemPrompt = options?.SystemPrompt;

        foreach (var msg in messages)
        {
            if (msg.Role == MessageRole.System)
            {
                systemPrompt = msg.Content;
                continue;
            }

            anthropicMessages.Add(new AnthropicMessage
            {
                Role = msg.Role switch
                {
                    MessageRole.User => "user",
                    MessageRole.Assistant => "assistant",
                    _ => "user"
                },
                Content = msg.ToolCalls != null
                    ? msg.ToolCalls.Select(tc => new AnthropicContent
                    {
                        Type = "tool_use",
                        Id = tc.Id,
                        Name = tc.Name,
                        Input = JsonSerializer.Deserialize<object>(tc.Arguments)
                    }).Cast<object>().ToList()
                    : msg.Role == MessageRole.Tool
                        ? new List<object> { new AnthropicContent
                        {
                            Type = "tool_result",
                            ToolUseId = msg.ToolCallId,
                            Content = msg.Content
                        }}
                        : new List<object> { new AnthropicContent { Type = "text", Text = msg.Content } }
            });
        }

        return new AnthropicRequest
        {
            Model = options?.Model ?? _options.DefaultModel ?? "claude-sonnet-4-20250514",
            Messages = anthropicMessages,
            System = systemPrompt,
            MaxTokens = options?.MaxTokens ?? _options.DefaultMaxTokens ?? 4096,
            Temperature = options?.Temperature ?? _options.DefaultTemperature,
            TopP = options?.TopP,
            StopSequences = options?.StopSequences?.ToList()
        };
    }

    private LlmResponse MapResponse(AnthropicResponse response)
    {
        var textContent = response.Content.FirstOrDefault(c => c.Type == "text");
        var toolUses = response.Content.Where(c => c.Type == "tool_use").ToList();

        return new LlmResponse
        {
            Content = textContent?.Text ?? string.Empty,
            ToolCalls = toolUses.Any() ? toolUses.Select(tc => new ToolCall
            {
                Id = tc.Id ?? string.Empty,
                Name = tc.Name ?? string.Empty,
                Arguments = tc.Input != null ? JsonSerializer.Serialize(tc.Input) : "{}"
            }).ToList() : null,
            Usage = response.Usage != null ? new CompletionUsage
            {
                PromptTokens = response.Usage.InputTokens,
                CompletionTokens = response.Usage.OutputTokens
            } : null,
            FinishReason = response.StopReason,
            ModelId = response.Model
        };
    }

    private string? ExtractStreamContent(string data)
    {
        try
        {
            var eventData = JsonSerializer.Deserialize<AnthropicStreamEvent>(data, JsonOptions);
            if (eventData?.Type == "content_block_delta" && eventData.Delta?.Type == "text_delta")
            {
                return eventData.Delta.Text;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}

public class AnthropicOptions
{
    public string ApiKey { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }
    public string? ApiVersion { get; init; } = "2023-06-01";
    public string? DefaultModel { get; init; } = "claude-sonnet-4-20250514";
    public double? DefaultTemperature { get; init; }
    public int? DefaultMaxTokens { get; init; } = 4096;
}

internal class AnthropicRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<AnthropicMessage> Messages { get; set; } = new();

    [JsonPropertyName("system")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? System { get; set; }

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 4096;

    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; set; }

    [JsonPropertyName("top_p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TopP { get; set; }

    [JsonPropertyName("stop_sequences")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? StopSequences { get; set; }

    [JsonPropertyName("stream")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Stream { get; set; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AnthropicTool>? Tools { get; set; }
}

internal class AnthropicMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public object Content { get; set; } = string.Empty;
}

internal class AnthropicContent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("input")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Input { get; set; }

    [JsonPropertyName("tool_use_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolUseId { get; set; }

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; set; }
}

internal class AnthropicTool
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("input_schema")]
    public AnthropicInputSchema InputSchema { get; set; } = new();
}

internal class AnthropicInputSchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, AnthropicProperty> Properties { get; set; } = new();

    [JsonPropertyName("required")]
    public List<string> Required { get; set; } = new();
}

internal class AnthropicProperty
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("enum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Enum { get; set; }
}

internal class AnthropicResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public List<AnthropicContent> Content { get; set; } = new();

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }

    [JsonPropertyName("usage")]
    public AnthropicUsage? Usage { get; set; }
}

internal class AnthropicUsage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }
}

internal class AnthropicStreamEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("delta")]
    public AnthropicDelta? Delta { get; set; }
}

internal class AnthropicDelta
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}
