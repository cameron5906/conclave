using Conclave.Deliberation;
using Conclave.Models;

namespace Conclave.Context;

public interface IContextManager
{
    ContextManagerType Type { get; }

    Task<ContextWindow> GetContextWindowAsync(
        DeliberationState state,
        string? agentId = null,
        ContextWindowOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<string> SummarizeAsync(
        IReadOnlyList<DeliberationMessage> messages,
        SummarizationOptions? options = null,
        CancellationToken cancellationToken = default);
}

public enum ContextManagerType
{
    SlidingWindow,
    RecursiveSummarization,
    Hierarchical,
    ObservationMasking,
    Hybrid
}

public record ContextWindowOptions
{
    public int? MaxTokens { get; init; }
    public int? MaxMessages { get; init; }
    public bool IncludeSystemMessages { get; init; } = true;
    public bool PreserveLatestRound { get; init; } = true;
    public double? CompressionRatio { get; init; }
}

public class ContextWindow
{
    public IReadOnlyList<Message> Messages { get; init; } = [];
    public string? Summary { get; init; }
    public int EstimatedTokenCount { get; init; }
    public int OriginalMessageCount { get; init; }
    public int RetainedMessageCount { get; init; }
    public double CompressionRatio => OriginalMessageCount > 0
        ? 1.0 - ((double)RetainedMessageCount / OriginalMessageCount)
        : 0;
    public ContextWindowMetadata Metadata { get; init; } = new();
}

public class ContextWindowMetadata
{
    public int MessagesDropped { get; init; }
    public int MessagesSummarized { get; init; }
    public int MessagesMasked { get; init; }
    public int RoundsPreserved { get; init; }
    public int RoundsSummarized { get; init; }
    public DateTime? OldestMessageTimestamp { get; init; }
    public DateTime? NewestMessageTimestamp { get; init; }
    public Dictionary<string, int> TokensByAgent { get; init; } = new();
}

public record SummarizationOptions
{
    public int? TargetTokenCount { get; init; }
    public double? CompressionRatio { get; init; }
    public bool PreserveKeyDecisions { get; init; } = true;
    public bool PreserveDisagreements { get; init; } = true;
    public SummarizationStyle Style { get; init; } = SummarizationStyle.Concise;
}

public enum SummarizationStyle
{
    Concise,
    Detailed,
    BulletPoints,
    Narrative
}
