using System.Text.RegularExpressions;
using Conclave.Abstractions;
using Conclave.Deliberation;
using Conclave.Models;

namespace Conclave.Context;

public class ObservationMaskingContextManager : BaseContextManager
{
    private readonly ObservationMaskingOptions _options;

    public ObservationMaskingContextManager(
        ObservationMaskingOptions? options = null,
        ILlmProvider? llmProvider = null)
        : base(llmProvider)
    {
        _options = options ?? new ObservationMaskingOptions();
    }

    public override ContextManagerType Type => ContextManagerType.ObservationMasking;

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

        var maskedMessages = new List<Message>();
        var maskedCount = 0;
        var currentRound = state.CurrentRound;

        foreach (var message in messages.OrderBy(m => m.Round).ThenBy(m => m.Timestamp))
        {
            var shouldMask = ShouldMaskMessage(message, currentRound, agentId);

            if (shouldMask)
            {
                var maskedContent = await MaskMessageContentAsync(message, cancellationToken);
                maskedMessages.Add(Message.Assistant(maskedContent) with { Name = message.AgentName });
                maskedCount++;
            }
            else
            {
                maskedMessages.Add(ConvertToMessage(message));
            }
        }

        var totalTokens = EstimateTokens(string.Join("\n", maskedMessages.Select(m => m.Content)));

        return new ContextWindow
        {
            Messages = maskedMessages,
            EstimatedTokenCount = totalTokens,
            OriginalMessageCount = messages.Count,
            RetainedMessageCount = messages.Count,
            Metadata = new ContextWindowMetadata
            {
                MessagesMasked = maskedCount,
                RoundsPreserved = messages.Select(m => m.Round).Distinct().Count(),
                OldestMessageTimestamp = messages.FirstOrDefault()?.Timestamp,
                NewestMessageTimestamp = messages.LastOrDefault()?.Timestamp,
                TokensByAgent = messages
                    .GroupBy(m => m.AgentId)
                    .ToDictionary(g => g.Key, g => EstimateTokens(g.ToList()))
            }
        };
    }

    private bool ShouldMaskMessage(DeliberationMessage message, int currentRound, string? targetAgentId)
    {
        if (_options.PreserveRecentRounds > 0 &&
            message.Round > currentRound - _options.PreserveRecentRounds)
        {
            return false;
        }

        if (_options.PreserveOwnMessages && message.AgentId == targetAgentId)
        {
            return false;
        }

        if (_options.AlwaysPreserveAgents.Contains(message.AgentId))
        {
            return false;
        }

        if (ContainsKeyDecision(message.Content))
        {
            return false;
        }

        var tokenCount = message.TokenCount > 0 ? message.TokenCount : EstimateTokens(message.Content);
        if (tokenCount > _options.VerbosityThreshold)
        {
            return true;
        }

        if (_options.MaskPatterns.Any(pattern => Regex.IsMatch(message.Content, pattern, RegexOptions.IgnoreCase)))
        {
            return true;
        }

        return _options.MaskByDefault;
    }

    private bool ContainsKeyDecision(string content)
    {
        var decisionIndicators = new[]
        {
            "I conclude", "my decision is", "I vote for", "I agree with",
            "I disagree with", "the answer is", "we should", "I recommend",
            "in summary", "to summarize", "final answer", "my position is"
        };

        return decisionIndicators.Any(indicator =>
            content.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> MaskMessageContentAsync(
        DeliberationMessage message,
        CancellationToken cancellationToken)
    {
        return _options.MaskingStrategy switch
        {
            MaskingStrategy.Truncate => TruncateContent(message.Content),
            MaskingStrategy.ExtractKeyPoints => await ExtractKeyPointsAsync(message, cancellationToken),
            MaskingStrategy.RemoveVerbose => RemoveVerboseContent(message.Content),
            MaskingStrategy.Placeholder => CreatePlaceholder(message),
            MaskingStrategy.Hybrid => await HybridMaskAsync(message, cancellationToken),
            _ => TruncateContent(message.Content)
        };
    }

    private string TruncateContent(string content)
    {
        var maxLength = _options.MaxMaskedLength;
        if (content.Length <= maxLength)
            return content;

        var sentences = content.Split(new[] { ". ", "! ", "? " }, StringSplitOptions.RemoveEmptyEntries);

        var result = new List<string>();
        var currentLength = 0;

        foreach (var sentence in sentences)
        {
            if (currentLength + sentence.Length > maxLength - 20)
                break;
            result.Add(sentence);
            currentLength += sentence.Length + 2;
        }

        if (result.Count == 0)
            return content[..Math.Min(maxLength, content.Length)] + "...";

        return string.Join(". ", result) + ". [truncated]";
    }

    private async Task<string> ExtractKeyPointsAsync(
        DeliberationMessage message,
        CancellationToken cancellationToken)
    {
        if (LlmProvider == null)
            return TruncateContent(message.Content);

        var prompt = $"""
            Extract only the key points from this agent's message. Be extremely concise.
            Focus on: decisions, positions, votes, and critical arguments.
            Ignore: verbose explanations, repetition, filler content.

            Message from {message.AgentName}:
            {message.Content}

            Key points (bullet form, max 3 points):
            """;

        var response = await LlmProvider.CompleteAsync(
            [Message.User(prompt)],
            new LlmCompletionOptions { Temperature = 0.2, MaxTokens = 150 },
            cancellationToken);

        return $"[Key points from {message.AgentName}]\n{response.Content}";
    }

    private string RemoveVerboseContent(string content)
    {
        var verbosePatterns = new[]
        {
            @"(?i)as I mentioned (earlier|before|previously)[^.]*\.",
            @"(?i)to elaborate (on this|further)[^.]*\.",
            @"(?i)in other words[^.]*\.",
            @"(?i)let me explain[^.]*\.",
            @"(?i)for example[^.]*\.",
            @"(?i)to clarify[^.]*\.",
            @"(?i)what I mean (is|by this)[^.]*\.",
            @"(?i)specifically[^.]*\.",
        };

        var result = content;
        foreach (var pattern in verbosePatterns)
        {
            result = Regex.Replace(result, pattern, " ");
        }

        result = Regex.Replace(result, @"\s+", " ").Trim();

        if (result.Length < content.Length * 0.5)
        {
            return result + " [condensed]";
        }

        return result;
    }

    private string CreatePlaceholder(DeliberationMessage message)
    {
        var wordCount = message.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var hasDecision = ContainsKeyDecision(message.Content);

        return $"[{message.AgentName} - Round {message.Round}: ~{wordCount} words" +
               (hasDecision ? ", contains decision" : "") + "]";
    }

    private async Task<string> HybridMaskAsync(
        DeliberationMessage message,
        CancellationToken cancellationToken)
    {
        var condensed = RemoveVerboseContent(message.Content);

        if (EstimateTokens(condensed) <= _options.MaxMaskedLength / 4)
        {
            return condensed;
        }

        return await ExtractKeyPointsAsync(message, cancellationToken);
    }

    public string MaskObservations(string content, params string[] sensitiveTerms)
    {
        var result = content;

        foreach (var term in sensitiveTerms)
        {
            var pattern = $@"\b{Regex.Escape(term)}\b";
            result = Regex.Replace(result, pattern, "[MASKED]", RegexOptions.IgnoreCase);
        }

        return result;
    }
}

public record ObservationMaskingOptions
{
    public MaskingStrategy MaskingStrategy { get; init; } = MaskingStrategy.Hybrid;
    public int VerbosityThreshold { get; init; } = 500;
    public int MaxMaskedLength { get; init; } = 200;
    public int PreserveRecentRounds { get; init; } = 1;
    public bool PreserveOwnMessages { get; init; } = true;
    public bool MaskByDefault { get; init; } = false;
    public HashSet<string> AlwaysPreserveAgents { get; init; } = [];
    public List<string> MaskPatterns { get; init; } = [
        @"(?i)verbose",
        @"(?i)detailed explanation",
        @"(?i)to elaborate"
    ];
    public List<string> SensitiveTerms { get; init; } = [];
}

public enum MaskingStrategy
{
    Truncate,
    ExtractKeyPoints,
    RemoveVerbose,
    Placeholder,
    Hybrid
}
