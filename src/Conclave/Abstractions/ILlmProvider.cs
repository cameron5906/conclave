using Conclave.Models;
using Conclave.Tools;

namespace Conclave.Abstractions;

public interface ILlmProvider
{
    string ProviderId { get; }
    string DisplayName { get; }

    Task<LlmResponse> CompleteAsync(
        IReadOnlyList<Message> messages,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<LlmResponse> CompleteWithToolsAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition> tools,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<Message> messages,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default);
}

public record LlmCompletionOptions
{
    public string? Model { get; init; }
    public double? Temperature { get; init; }
    public int? MaxTokens { get; init; }
    public double? TopP { get; init; }
    public double? FrequencyPenalty { get; init; }
    public double? PresencePenalty { get; init; }
    public IReadOnlyList<string>? StopSequences { get; init; }
    public string? SystemPrompt { get; init; }
    public object? ResponseFormat { get; init; }
}
