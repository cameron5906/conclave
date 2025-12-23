using Conclave.Abstractions;
using Conclave.Deliberation;
using Conclave.Models;

namespace Conclave.Context;

public class HybridContextManager : BaseContextManager
{
    private readonly HybridContextOptions _options;
    private readonly SlidingWindowContextManager _slidingWindow;
    private readonly RecursiveSummarizationContextManager? _recursiveSummarization;
    private readonly ObservationMaskingContextManager _observationMasking;

    public HybridContextManager(
        ILlmProvider llmProvider,
        HybridContextOptions? options = null)
        : base(llmProvider)
    {
        _options = options ?? new HybridContextOptions();

        _slidingWindow = new SlidingWindowContextManager(
            new SlidingWindowOptions
            {
                MaxTokens = _options.SlidingWindowTokens,
                PreserveLatestRound = true,
                PreserveFirstRound = _options.PreserveFirstRound
            });

        _recursiveSummarization = llmProvider != null
            ? new RecursiveSummarizationContextManager(
                llmProvider,
                new RecursiveSummarizationOptions
                {
                    MaxTokenBudget = _options.SummarizationBudget,
                    PreserveRecentRounds = _options.PreserveRecentRounds
                })
            : null;

        _observationMasking = new ObservationMaskingContextManager(
            new ObservationMaskingOptions
            {
                MaskingStrategy = _options.MaskingStrategy,
                VerbosityThreshold = _options.VerbosityThreshold,
                PreserveRecentRounds = _options.PreserveRecentRounds
            },
            llmProvider);
    }

    public override ContextManagerType Type => ContextManagerType.Hybrid;

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

        var totalTokens = EstimateTokens(messages);
        var maxTokens = options?.MaxTokens ?? _options.MaxTokenBudget;

        if (totalTokens <= maxTokens)
        {
            return new ContextWindow
            {
                Messages = ConvertToMessages(messages),
                EstimatedTokenCount = totalTokens,
                OriginalMessageCount = messages.Count,
                RetainedMessageCount = messages.Count,
                Metadata = new ContextWindowMetadata
                {
                    RoundsPreserved = messages.Select(m => m.Round).Distinct().Count()
                }
            };
        }

        var strategy = DetermineOptimalStrategy(state, totalTokens, maxTokens);

        return strategy switch
        {
            HybridStrategy.SlidingWindowOnly => await _slidingWindow.GetContextWindowAsync(
                state, agentId, options, cancellationToken),

            HybridStrategy.MaskThenWindow => await ApplyMaskThenWindowAsync(
                state, agentId, options, cancellationToken),

            HybridStrategy.SummarizeThenMask => await ApplySummarizeThenMaskAsync(
                state, agentId, options, cancellationToken),

            HybridStrategy.FullPipeline => await ApplyFullPipelineAsync(
                state, agentId, options, cancellationToken),

            _ => await _slidingWindow.GetContextWindowAsync(
                state, agentId, options, cancellationToken)
        };
    }

    private HybridStrategy DetermineOptimalStrategy(
        DeliberationState state,
        int currentTokens,
        int targetTokens)
    {
        var compressionNeeded = (double)currentTokens / targetTokens;
        var roundCount = state.Transcript.Select(m => m.Round).Distinct().Count();

        if (compressionNeeded <= 1.5)
        {
            return HybridStrategy.SlidingWindowOnly;
        }

        if (compressionNeeded <= 2.5)
        {
            return HybridStrategy.MaskThenWindow;
        }

        if (roundCount > 5 && _recursiveSummarization != null)
        {
            return HybridStrategy.SummarizeThenMask;
        }

        return HybridStrategy.FullPipeline;
    }

    private async Task<ContextWindow> ApplyMaskThenWindowAsync(
        DeliberationState state,
        string? agentId,
        ContextWindowOptions? options,
        CancellationToken cancellationToken)
    {
        var maskedContext = await _observationMasking.GetContextWindowAsync(
            state, agentId, options, cancellationToken);

        var maskedTokens = maskedContext.EstimatedTokenCount;
        var maxTokens = options?.MaxTokens ?? _options.MaxTokenBudget;

        if (maskedTokens <= maxTokens)
        {
            return maskedContext;
        }

        var windowOptions = (options ?? new ContextWindowOptions()) with { MaxTokens = maxTokens };
        return await _slidingWindow.GetContextWindowAsync(
            state, agentId, windowOptions, cancellationToken);
    }

    private async Task<ContextWindow> ApplySummarizeThenMaskAsync(
        DeliberationState state,
        string? agentId,
        ContextWindowOptions? options,
        CancellationToken cancellationToken)
    {
        if (_recursiveSummarization == null)
        {
            return await ApplyMaskThenWindowAsync(state, agentId, options, cancellationToken);
        }

        var summarizedContext = await _recursiveSummarization.GetContextWindowAsync(
            state, agentId, options, cancellationToken);

        return summarizedContext;
    }

    private async Task<ContextWindow> ApplyFullPipelineAsync(
        DeliberationState state,
        string? agentId,
        ContextWindowOptions? options,
        CancellationToken cancellationToken)
    {
        var maxTokens = options?.MaxTokens ?? _options.MaxTokenBudget;

        var maskedContext = await _observationMasking.GetContextWindowAsync(
            state, agentId, options, cancellationToken);

        if (maskedContext.EstimatedTokenCount <= maxTokens)
        {
            return maskedContext;
        }

        if (_recursiveSummarization != null)
        {
            var summarizedContext = await _recursiveSummarization.GetContextWindowAsync(
                state, agentId, options, cancellationToken);

            if (summarizedContext.EstimatedTokenCount <= maxTokens)
            {
                return summarizedContext;
            }
        }

        var windowOptions = (options ?? new ContextWindowOptions()) with { MaxTokens = maxTokens };
        return await _slidingWindow.GetContextWindowAsync(
            state, agentId, windowOptions, cancellationToken);
    }
}

public record HybridContextOptions
{
    public int MaxTokenBudget { get; init; } = 8000;
    public int SlidingWindowTokens { get; init; } = 6000;
    public int SummarizationBudget { get; init; } = 2000;
    public int VerbosityThreshold { get; init; } = 500;
    public int PreserveRecentRounds { get; init; } = 2;
    public bool PreserveFirstRound { get; init; } = true;
    public MaskingStrategy MaskingStrategy { get; init; } = MaskingStrategy.Hybrid;
    public bool AutoSelectStrategy { get; init; } = true;
}

internal enum HybridStrategy
{
    SlidingWindowOnly,
    MaskThenWindow,
    SummarizeThenMask,
    FullPipeline
}
