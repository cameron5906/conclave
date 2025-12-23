using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Conclave.Abstractions;
using Conclave.Models;
using Conclave.Tools;
using Microsoft.Extensions.Logging;

namespace Conclave.Providers;

public class GeminiProvider : BaseLlmProvider
{
    private readonly GeminiOptions _options;

    public override string ProviderId => "gemini";
    public override string DisplayName => "Google Gemini";

    public GeminiProvider(HttpClient httpClient, GeminiOptions options, ILogger<GeminiProvider>? logger = null)
        : base(httpClient, logger)
    {
        _options = options;
        httpClient.BaseAddress = new Uri(options.BaseUrl ?? "https://generativelanguage.googleapis.com/v1beta/");
    }

    public override async Task<LlmResponse> CompleteAsync(
        IReadOnlyList<Message> messages,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var model = options?.Model ?? _options.DefaultModel ?? "gemini-2.0-flash";
        var request = BuildRequest(messages, options);
        var endpoint = $"models/{model}:generateContent?key={_options.ApiKey}";
        var response = await SendRequestAsync<GeminiResponse>(endpoint, request, cancellationToken);
        return MapResponse(response);
    }

    public override async Task<LlmResponse> CompleteWithToolsAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition> tools,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var model = options?.Model ?? _options.DefaultModel ?? "gemini-2.0-flash";
        var request = BuildRequest(messages, options);
        request.Tools = new List<GeminiToolSet>
        {
            new()
            {
                FunctionDeclarations = tools.Select(t => new GeminiFunctionDeclaration
                {
                    Name = t.Name,
                    Description = t.Description,
                    Parameters = new GeminiSchema
                    {
                        Type = "object",
                        Properties = t.Parameters.Properties.ToDictionary(
                            p => p.Key,
                            p => new GeminiSchemaProperty
                            {
                                Type = p.Value.Type,
                                Description = p.Value.Description,
                                Enum = p.Value.Enum
                            }),
                        Required = t.Parameters.Required
                    }
                }).ToList()
            }
        };

        var endpoint = $"models/{model}:generateContent?key={_options.ApiKey}";
        var response = await SendRequestAsync<GeminiResponse>(endpoint, request, cancellationToken);
        return MapResponse(response);
    }

    public override async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<Message> messages,
        LlmCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var model = options?.Model ?? _options.DefaultModel ?? "gemini-2.0-flash";
        var request = BuildRequest(messages, options);
        var endpoint = $"models/{model}:streamGenerateContent?key={_options.ApiKey}&alt=sse";

        await foreach (var chunk in StreamRequestAsync(endpoint, request, ExtractStreamContent, cancellationToken))
        {
            yield return chunk;
        }
    }

    private GeminiRequest BuildRequest(IReadOnlyList<Message> messages, LlmCompletionOptions? options)
    {
        var geminiContents = new List<GeminiContent>();
        GeminiContent? systemInstruction = null;

        foreach (var msg in messages)
        {
            if (msg.Role == MessageRole.System)
            {
                systemInstruction = new GeminiContent
                {
                    Parts = new List<GeminiPart> { new() { Text = msg.Content } }
                };
                continue;
            }

            var content = new GeminiContent
            {
                Role = msg.Role switch
                {
                    MessageRole.User => "user",
                    MessageRole.Assistant => "model",
                    MessageRole.Tool => "function",
                    _ => "user"
                },
                Parts = new List<GeminiPart>()
            };

            if (msg.ToolCalls != null)
            {
                foreach (var tc in msg.ToolCalls)
                {
                    content.Parts.Add(new GeminiPart
                    {
                        FunctionCall = new GeminiFunctionCall
                        {
                            Name = tc.Name,
                            Args = JsonSerializer.Deserialize<Dictionary<string, object>>(tc.Arguments) ?? new()
                        }
                    });
                }
            }
            else if (msg.Role == MessageRole.Tool)
            {
                content.Parts.Add(new GeminiPart
                {
                    FunctionResponse = new GeminiFunctionResponse
                    {
                        Name = msg.Name ?? "function",
                        Response = new Dictionary<string, object> { ["result"] = msg.Content }
                    }
                });
            }
            else
            {
                content.Parts.Add(new GeminiPart { Text = msg.Content });
            }

            geminiContents.Add(content);
        }

        if (!string.IsNullOrEmpty(options?.SystemPrompt))
        {
            systemInstruction = new GeminiContent
            {
                Parts = new List<GeminiPart> { new() { Text = options.SystemPrompt } }
            };
        }

        return new GeminiRequest
        {
            Contents = geminiContents,
            SystemInstruction = systemInstruction,
            GenerationConfig = new GeminiGenerationConfig
            {
                Temperature = options?.Temperature ?? _options.DefaultTemperature,
                MaxOutputTokens = options?.MaxTokens ?? _options.DefaultMaxTokens,
                TopP = options?.TopP,
                StopSequences = options?.StopSequences?.ToList()
            }
        };
    }

    private LlmResponse MapResponse(GeminiResponse response)
    {
        var candidate = response.Candidates?.FirstOrDefault();
        var content = candidate?.Content;
        var textPart = content?.Parts?.FirstOrDefault(p => p.Text != null);
        var functionCalls = content?.Parts?.Where(p => p.FunctionCall != null).ToList();

        return new LlmResponse
        {
            Content = textPart?.Text ?? string.Empty,
            ToolCalls = functionCalls?.Any() == true ? functionCalls.Select(fc => new ToolCall
            {
                Id = Guid.NewGuid().ToString(),
                Name = fc.FunctionCall!.Name,
                Arguments = JsonSerializer.Serialize(fc.FunctionCall.Args)
            }).ToList() : null,
            Usage = response.UsageMetadata != null ? new CompletionUsage
            {
                PromptTokens = response.UsageMetadata.PromptTokenCount,
                CompletionTokens = response.UsageMetadata.CandidatesTokenCount
            } : null,
            FinishReason = candidate?.FinishReason,
            ModelId = response.ModelVersion
        };
    }

    private string? ExtractStreamContent(string data)
    {
        try
        {
            var chunk = JsonSerializer.Deserialize<GeminiResponse>(data, JsonOptions);
            return chunk?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        }
        catch
        {
            return null;
        }
    }
}

public class GeminiOptions
{
    public string ApiKey { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }
    public string? DefaultModel { get; init; } = "gemini-2.0-flash";
    public double? DefaultTemperature { get; init; }
    public int? DefaultMaxTokens { get; init; }
}

internal class GeminiRequest
{
    [JsonPropertyName("contents")]
    public List<GeminiContent> Contents { get; set; } = new();

    [JsonPropertyName("systemInstruction")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiContent? SystemInstruction { get; set; }

    [JsonPropertyName("generationConfig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiGenerationConfig? GenerationConfig { get; set; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<GeminiToolSet>? Tools { get; set; }
}

internal class GeminiContent
{
    [JsonPropertyName("role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Role { get; set; }

    [JsonPropertyName("parts")]
    public List<GeminiPart> Parts { get; set; } = new();
}

internal class GeminiPart
{
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("functionCall")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiFunctionCall? FunctionCall { get; set; }

    [JsonPropertyName("functionResponse")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiFunctionResponse? FunctionResponse { get; set; }
}

internal class GeminiFunctionCall
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("args")]
    public Dictionary<string, object> Args { get; set; } = new();
}

internal class GeminiFunctionResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("response")]
    public Dictionary<string, object> Response { get; set; } = new();
}

internal class GeminiGenerationConfig
{
    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; set; }

    [JsonPropertyName("maxOutputTokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxOutputTokens { get; set; }

    [JsonPropertyName("topP")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TopP { get; set; }

    [JsonPropertyName("stopSequences")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? StopSequences { get; set; }
}

internal class GeminiToolSet
{
    [JsonPropertyName("functionDeclarations")]
    public List<GeminiFunctionDeclaration> FunctionDeclarations { get; set; } = new();
}

internal class GeminiFunctionDeclaration
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public GeminiSchema Parameters { get; set; } = new();
}

internal class GeminiSchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, GeminiSchemaProperty> Properties { get; set; } = new();

    [JsonPropertyName("required")]
    public List<string> Required { get; set; } = new();
}

internal class GeminiSchemaProperty
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("enum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Enum { get; set; }
}

internal class GeminiResponse
{
    [JsonPropertyName("candidates")]
    public List<GeminiCandidate>? Candidates { get; set; }

    [JsonPropertyName("usageMetadata")]
    public GeminiUsageMetadata? UsageMetadata { get; set; }

    [JsonPropertyName("modelVersion")]
    public string? ModelVersion { get; set; }
}

internal class GeminiCandidate
{
    [JsonPropertyName("content")]
    public GeminiContent? Content { get; set; }

    [JsonPropertyName("finishReason")]
    public string? FinishReason { get; set; }
}

internal class GeminiUsageMetadata
{
    [JsonPropertyName("promptTokenCount")]
    public int PromptTokenCount { get; set; }

    [JsonPropertyName("candidatesTokenCount")]
    public int CandidatesTokenCount { get; set; }
}
