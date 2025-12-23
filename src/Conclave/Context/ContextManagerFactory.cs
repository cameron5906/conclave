using Conclave.Abstractions;

namespace Conclave.Context;

public static class ContextManagerFactory
{
    public static IContextManager CreateSlidingWindow(
        int? maxTokens = null,
        int? maxMessages = null,
        bool preserveFirstRound = true,
        bool preserveLatestRound = true)
    {
        return new SlidingWindowContextManager(new SlidingWindowOptions
        {
            MaxTokens = maxTokens ?? 8000,
            MaxMessages = maxMessages,
            PreserveFirstRound = preserveFirstRound,
            PreserveLatestRound = preserveLatestRound
        });
    }

    public static IContextManager CreateRecursiveSummarization(
        ILlmProvider llmProvider,
        int? maxTokenBudget = null,
        int preserveRecentRounds = 2,
        double compressionRatio = 0.5)
    {
        return new RecursiveSummarizationContextManager(llmProvider, new RecursiveSummarizationOptions
        {
            MaxTokenBudget = maxTokenBudget ?? 8000,
            PreserveRecentRounds = preserveRecentRounds,
            CompressionRatio = compressionRatio
        });
    }

    public static IContextManager CreateHierarchical(
        ILlmProvider llmProvider,
        int? maxTokenBudget = null,
        int roundsPerPhase = 3,
        PhaseNamingStyle namingStyle = PhaseNamingStyle.Descriptive)
    {
        return new HierarchicalContextManager(llmProvider, new HierarchicalContextOptions
        {
            MaxTokenBudget = maxTokenBudget ?? 8000,
            RoundsPerPhase = roundsPerPhase,
            PhaseNamingStyle = namingStyle
        });
    }

    public static IContextManager CreateObservationMasking(
        MaskingStrategy strategy = MaskingStrategy.Hybrid,
        int verbosityThreshold = 500,
        int preserveRecentRounds = 1,
        ILlmProvider? llmProvider = null)
    {
        return new ObservationMaskingContextManager(new ObservationMaskingOptions
        {
            MaskingStrategy = strategy,
            VerbosityThreshold = verbosityThreshold,
            PreserveRecentRounds = preserveRecentRounds
        }, llmProvider);
    }

    public static IContextManager CreateHybrid(
        ILlmProvider llmProvider,
        int maxTokenBudget = 8000,
        bool autoSelectStrategy = true)
    {
        return new HybridContextManager(llmProvider, new HybridContextOptions
        {
            MaxTokenBudget = maxTokenBudget,
            AutoSelectStrategy = autoSelectStrategy
        });
    }

    public static IContextManager Create(
        ContextManagerType type,
        ILlmProvider? llmProvider = null,
        int? maxTokenBudget = null)
    {
        return type switch
        {
            ContextManagerType.SlidingWindow => CreateSlidingWindow(maxTokenBudget),
            ContextManagerType.RecursiveSummarization when llmProvider != null =>
                CreateRecursiveSummarization(llmProvider, maxTokenBudget),
            ContextManagerType.Hierarchical when llmProvider != null =>
                CreateHierarchical(llmProvider, maxTokenBudget),
            ContextManagerType.ObservationMasking =>
                CreateObservationMasking(llmProvider: llmProvider),
            ContextManagerType.Hybrid when llmProvider != null =>
                CreateHybrid(llmProvider, maxTokenBudget ?? 8000),
            _ => CreateSlidingWindow(maxTokenBudget)
        };
    }
}
