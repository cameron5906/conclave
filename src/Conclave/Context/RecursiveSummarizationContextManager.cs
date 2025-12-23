using Conclave.Abstractions;
using Conclave.Deliberation;
using Conclave.Models;

namespace Conclave.Context;

public class RecursiveSummarizationContextManager : BaseContextManager
{
    private readonly RecursiveSummarizationOptions _options;
    private readonly Dictionary<int, string> _roundSummaryCache = new();

    public RecursiveSummarizationContextManager(
        ILlmProvider llmProvider,
        RecursiveSummarizationOptions? options = null)
        : base(llmProvider)
    {
        _options = options ?? new RecursiveSummarizationOptions();
    }

    public override ContextManagerType Type => ContextManagerType.RecursiveSummarization;

    public override async Task<ContextWindow> GetContextWindowAsync(
        DeliberationState state,
        string? agentId = null,
        ContextWindowOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messages = agentId != null
            ? state.GetMessagesForAgent(agentId)
            : state.Transcript.ToList();

        if (messages.Count == 0)
        {
            return new ContextWindow
            {
                Messages = [],
                EstimatedTokenCount = 0,
                OriginalMessageCount = 0,
                RetainedMessageCount = 0
            };
        }

        var maxTokens = options?.MaxTokens ?? _options.MaxTokenBudget;
        var preserveRounds = _options.PreserveRecentRounds;

        var messagesByRound = messages
            .GroupBy(m => m.Round)
            .OrderBy(g => g.Key)
            .ToList();

        var currentRound = state.CurrentRound;
        var roundsToPreserve = messagesByRound
            .Where(g => g.Key > currentRound - preserveRounds)
            .ToList();
        var roundsToSummarize = messagesByRound
            .Where(g => g.Key <= currentRound - preserveRounds)
            .ToList();

        var summaryMessages = new List<Message>();
        var summarizedCount = 0;

        if (roundsToSummarize.Any())
        {
            var summary = await GetOrCreateSummaryAsync(
                roundsToSummarize.SelectMany(g => g).ToList(),
                currentRound - preserveRounds,
                cancellationToken);

            summaryMessages.Add(Message.System($"[Summary of rounds 1-{currentRound - preserveRounds}]\n{summary}"));
            summarizedCount = roundsToSummarize.Sum(g => g.Count());
        }

        var preservedMessages = roundsToPreserve
            .SelectMany(g => g)
            .OrderBy(m => m.Round)
            .ThenBy(m => m.Timestamp)
            .ToList();

        var allMessages = summaryMessages
            .Concat(ConvertToMessages(preservedMessages))
            .ToList();

        var totalTokens = EstimateTokens(string.Join("\n", allMessages.Select(m => m.Content)));

        if (maxTokens.HasValue && totalTokens > maxTokens.Value)
        {
            allMessages = await CompressRecursivelyAsync(
                allMessages,
                maxTokens.Value,
                cancellationToken);
            totalTokens = EstimateTokens(string.Join("\n", allMessages.Select(m => m.Content)));
        }

        return new ContextWindow
        {
            Messages = allMessages,
            Summary = summaryMessages.FirstOrDefault()?.Content,
            EstimatedTokenCount = totalTokens,
            OriginalMessageCount = messages.Count,
            RetainedMessageCount = preservedMessages.Count,
            Metadata = new ContextWindowMetadata
            {
                MessagesSummarized = summarizedCount,
                MessagesDropped = 0,
                RoundsPreserved = roundsToPreserve.Count,
                RoundsSummarized = roundsToSummarize.Count,
                OldestMessageTimestamp = preservedMessages.FirstOrDefault()?.Timestamp,
                NewestMessageTimestamp = preservedMessages.LastOrDefault()?.Timestamp
            }
        };
    }

    private async Task<string> GetOrCreateSummaryAsync(
        IReadOnlyList<DeliberationMessage> messages,
        int throughRound,
        CancellationToken cancellationToken)
    {
        if (_roundSummaryCache.TryGetValue(throughRound, out var cached))
            return cached;

        var previousRoundEnd = throughRound - _options.SummarizationChunkSize;
        string? previousSummary = null;

        if (previousRoundEnd > 0 && _roundSummaryCache.TryGetValue(previousRoundEnd, out var prevCached))
        {
            previousSummary = prevCached;
            messages = messages.Where(m => m.Round > previousRoundEnd).ToList();
        }

        var summary = await CreateIncrementalSummaryAsync(
            messages,
            previousSummary,
            cancellationToken);

        _roundSummaryCache[throughRound] = summary;
        return summary;
    }

    private async Task<string> CreateIncrementalSummaryAsync(
        IReadOnlyList<DeliberationMessage> messages,
        string? previousSummary,
        CancellationToken cancellationToken)
    {
        if (LlmProvider == null)
            return CreateFallbackSummary(messages, null);

        var transcript = FormatMessagesForSummary(messages);

        var prompt = previousSummary != null
            ? $"""
              You are summarizing an ongoing multi-agent deliberation.

              Previous summary:
              {previousSummary}

              New messages to incorporate:
              {transcript}

              Create an updated summary that:
              1. Incorporates the key points from the new messages
              2. Preserves important context from the previous summary
              3. Tracks any shifts in agent positions
              4. Notes any emerging consensus or persistent disagreements

              Updated summary:
              """
            : $"""
              Summarize the following multi-agent deliberation transcript.
              Focus on:
              1. Key arguments and positions of each agent
              2. Areas of agreement and disagreement
              3. Important decisions or conclusions reached

              Transcript:
              {transcript}

              Summary:
              """;

        var response = await LlmProvider.CompleteAsync(
            [Message.User(prompt)],
            new LlmCompletionOptions { Temperature = 0.3 },
            cancellationToken);

        return response.Content;
    }

    private async Task<List<Message>> CompressRecursivelyAsync(
        List<Message> messages,
        int targetTokens,
        CancellationToken cancellationToken)
    {
        var currentTokens = EstimateTokens(string.Join("\n", messages.Select(m => m.Content)));

        if (currentTokens <= targetTokens || messages.Count <= 1)
            return messages;

        var midpoint = messages.Count / 2;
        var firstHalf = messages.Take(midpoint).ToList();
        var secondHalf = messages.Skip(midpoint).ToList();

        var firstHalfSummary = await SummarizeMessagesAsync(firstHalf, cancellationToken);

        var result = new List<Message> { Message.System($"[Compressed context]\n{firstHalfSummary}") };
        result.AddRange(secondHalf);

        currentTokens = EstimateTokens(string.Join("\n", result.Select(m => m.Content)));

        if (currentTokens > targetTokens && result.Count > 2)
        {
            return await CompressRecursivelyAsync(result, targetTokens, cancellationToken);
        }

        return result;
    }

    private async Task<string> SummarizeMessagesAsync(
        List<Message> messages,
        CancellationToken cancellationToken)
    {
        if (LlmProvider == null)
            return $"[Summary of {messages.Count} messages]";

        var content = string.Join("\n\n", messages.Select(m =>
            m.Role == MessageRole.System ? $"[System]: {m.Content}" : $"[{m.Name ?? "Agent"}]: {m.Content}"));

        var prompt = $"""
            Summarize the following conversation segment concisely, preserving key points:

            {content}

            Concise summary:
            """;

        var response = await LlmProvider.CompleteAsync(
            [Message.User(prompt)],
            new LlmCompletionOptions { Temperature = 0.3 },
            cancellationToken);

        return response.Content;
    }

    public void ClearCache()
    {
        _roundSummaryCache.Clear();
    }
}

public record RecursiveSummarizationOptions
{
    public int? MaxTokenBudget { get; init; } = 8000;
    public int PreserveRecentRounds { get; init; } = 2;
    public int SummarizationChunkSize { get; init; } = 3;
    public double CompressionRatio { get; init; } = 0.5;
    public bool PreserveKeyDecisions { get; init; } = true;
    public bool PreserveDisagreements { get; init; } = true;
}
