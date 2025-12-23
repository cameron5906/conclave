using Conclave.Abstractions;
using Conclave.Deliberation;
using Conclave.Models;

namespace Conclave.Context;

public class SlidingWindowContextManager : BaseContextManager
{
    private readonly SlidingWindowOptions _windowOptions;

    public SlidingWindowContextManager(SlidingWindowOptions? options = null, ILlmProvider? llmProvider = null)
        : base(llmProvider)
    {
        _windowOptions = options ?? new SlidingWindowOptions();
    }

    public override ContextManagerType Type => ContextManagerType.SlidingWindow;

    public override Task<ContextWindow> GetContextWindowAsync(
        DeliberationState state,
        string? agentId = null,
        ContextWindowOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messages = agentId != null
            ? state.GetMessagesForAgent(agentId)
            : state.Transcript.ToList();

        var maxTokens = options?.MaxTokens ?? _windowOptions.MaxTokens;
        var maxMessages = options?.MaxMessages ?? _windowOptions.MaxMessages;
        var preserveLatestRound = options?.PreserveLatestRound ?? _windowOptions.PreserveLatestRound;
        var preserveFirstRound = _windowOptions.PreserveFirstRound;

        var selectedMessages = SelectMessagesWithinWindow(
            messages,
            state.CurrentRound,
            maxTokens,
            maxMessages,
            preserveLatestRound,
            preserveFirstRound);

        var result = new ContextWindow
        {
            Messages = ConvertToMessages(selectedMessages),
            EstimatedTokenCount = EstimateTokens(selectedMessages),
            OriginalMessageCount = messages.Count,
            RetainedMessageCount = selectedMessages.Count,
            Metadata = new ContextWindowMetadata
            {
                MessagesDropped = messages.Count - selectedMessages.Count,
                RoundsPreserved = selectedMessages.Select(m => m.Round).Distinct().Count(),
                OldestMessageTimestamp = selectedMessages.FirstOrDefault()?.Timestamp,
                NewestMessageTimestamp = selectedMessages.LastOrDefault()?.Timestamp,
                TokensByAgent = selectedMessages
                    .GroupBy(m => m.AgentId)
                    .ToDictionary(g => g.Key, g => EstimateTokens(g.ToList()))
            }
        };

        return Task.FromResult(result);
    }

    private List<DeliberationMessage> SelectMessagesWithinWindow(
        IReadOnlyList<DeliberationMessage> messages,
        int currentRound,
        int? maxTokens,
        int? maxMessages,
        bool preserveLatestRound,
        bool preserveFirstRound)
    {
        if (messages.Count == 0)
            return [];

        var orderedMessages = messages.OrderBy(m => m.Round).ThenBy(m => m.Timestamp).ToList();
        var result = new List<DeliberationMessage>();

        var firstRoundMessages = preserveFirstRound
            ? orderedMessages.Where(m => m.Round == 1).ToList()
            : [];

        var latestRoundMessages = preserveLatestRound
            ? orderedMessages.Where(m => m.Round == currentRound - 1 || m.Round == currentRound).ToList()
            : [];

        var preservedMessages = firstRoundMessages
            .Concat(latestRoundMessages)
            .DistinctBy(m => (m.AgentId, m.Round, m.Timestamp))
            .ToList();

        var middleMessages = orderedMessages
            .Except(preservedMessages)
            .OrderByDescending(m => m.Round)
            .ThenByDescending(m => m.Timestamp)
            .ToList();

        result.AddRange(preservedMessages);
        int currentTokenCount = EstimateTokens(result);

        foreach (var message in middleMessages)
        {
            if (maxMessages.HasValue && result.Count >= maxMessages.Value)
                break;

            var messageTokens = message.TokenCount > 0 ? message.TokenCount : EstimateTokens(message.Content);
            if (maxTokens.HasValue && currentTokenCount + messageTokens > maxTokens.Value)
                continue;

            result.Add(message);
            currentTokenCount += messageTokens;
        }

        return result.OrderBy(m => m.Round).ThenBy(m => m.Timestamp).ToList();
    }
}

public record SlidingWindowOptions
{
    public int? MaxTokens { get; init; } = 8000;
    public int? MaxMessages { get; init; }
    public bool PreserveLatestRound { get; init; } = true;
    public bool PreserveFirstRound { get; init; } = true;
    public int WindowRounds { get; init; } = 3;
}
