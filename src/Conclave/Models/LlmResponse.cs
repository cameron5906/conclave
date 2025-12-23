namespace Conclave.Models;

public class LlmResponse
{
    public string Content { get; init; } = string.Empty;
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }
    public CompletionUsage? Usage { get; init; }
    public string? FinishReason { get; init; }
    public string? ModelId { get; init; }
}

public class CompletionUsage
{
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens => PromptTokens + CompletionTokens;
}
