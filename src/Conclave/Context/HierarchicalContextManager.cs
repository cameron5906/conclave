using Conclave.Abstractions;
using Conclave.Deliberation;
using Conclave.Models;

namespace Conclave.Context;

public class HierarchicalContextManager : BaseContextManager
{
    private readonly HierarchicalContextOptions _options;
    private readonly Dictionary<string, HierarchySummaryNode> _hierarchyCache = new();

    public HierarchicalContextManager(
        ILlmProvider llmProvider,
        HierarchicalContextOptions? options = null)
        : base(llmProvider)
    {
        _options = options ?? new HierarchicalContextOptions();
    }

    public override ContextManagerType Type => ContextManagerType.Hierarchical;

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
        var hierarchy = await BuildHierarchyAsync(messages, state.CurrentRound, cancellationToken);

        var selectedContent = SelectFromHierarchy(hierarchy, maxTokens ?? 8000);

        var resultMessages = new List<Message>();

        if (!string.IsNullOrEmpty(selectedContent.GlobalSummary))
        {
            resultMessages.Add(Message.System($"[Deliberation Overview]\n{selectedContent.GlobalSummary}"));
        }

        foreach (var phaseSummary in selectedContent.PhaseSummaries)
        {
            resultMessages.Add(Message.System($"[{phaseSummary.Key}]\n{phaseSummary.Value}"));
        }

        resultMessages.AddRange(ConvertToMessages(selectedContent.DetailedMessages));

        var totalTokens = EstimateTokens(string.Join("\n", resultMessages.Select(m => m.Content)));

        return new ContextWindow
        {
            Messages = resultMessages,
            Summary = selectedContent.GlobalSummary,
            EstimatedTokenCount = totalTokens,
            OriginalMessageCount = messages.Count,
            RetainedMessageCount = selectedContent.DetailedMessages.Count,
            Metadata = new ContextWindowMetadata
            {
                MessagesSummarized = messages.Count - selectedContent.DetailedMessages.Count,
                RoundsPreserved = selectedContent.DetailedMessages.Select(m => m.Round).Distinct().Count(),
                RoundsSummarized = hierarchy.Phases.Count,
                OldestMessageTimestamp = messages.FirstOrDefault()?.Timestamp,
                NewestMessageTimestamp = messages.LastOrDefault()?.Timestamp
            }
        };
    }

    private async Task<ConversationHierarchy> BuildHierarchyAsync(
        IReadOnlyList<DeliberationMessage> messages,
        int currentRound,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"{messages.Count}_{currentRound}";
        if (_hierarchyCache.TryGetValue(cacheKey, out var cachedRoot))
        {
            return new ConversationHierarchy { Root = cachedRoot };
        }

        var phases = SegmentIntoPhases(messages, currentRound);
        var hierarchy = new ConversationHierarchy();

        foreach (var phase in phases)
        {
            var phaseSummary = await SummarizePhaseAsync(phase.Value, phase.Key, cancellationToken);
            hierarchy.Phases[phase.Key] = new HierarchySummaryNode
            {
                Level = HierarchyLevel.Phase,
                Summary = phaseSummary,
                Messages = phase.Value,
                TokenCount = EstimateTokens(phaseSummary)
            };
        }

        if (hierarchy.Phases.Count > 1)
        {
            var globalSummary = await CreateGlobalSummaryAsync(hierarchy.Phases, cancellationToken);
            hierarchy.Root = new HierarchySummaryNode
            {
                Level = HierarchyLevel.Global,
                Summary = globalSummary,
                TokenCount = EstimateTokens(globalSummary)
            };
            _hierarchyCache[cacheKey] = hierarchy.Root;
        }

        return hierarchy;
    }

    private Dictionary<string, List<DeliberationMessage>> SegmentIntoPhases(
        IReadOnlyList<DeliberationMessage> messages,
        int currentRound)
    {
        var phases = new Dictionary<string, List<DeliberationMessage>>();
        var roundsPerPhase = _options.RoundsPerPhase;

        var messagesByRound = messages.GroupBy(m => m.Round).OrderBy(g => g.Key).ToList();

        var phaseNumber = 1;
        var phaseMessages = new List<DeliberationMessage>();
        var roundsInPhase = 0;

        foreach (var roundGroup in messagesByRound)
        {
            phaseMessages.AddRange(roundGroup);
            roundsInPhase++;

            if (roundsInPhase >= roundsPerPhase || roundGroup.Key == currentRound - 1)
            {
                var phaseName = GetPhaseName(phaseNumber, roundGroup.Key, currentRound);
                phases[phaseName] = phaseMessages.ToList();
                phaseMessages.Clear();
                roundsInPhase = 0;
                phaseNumber++;
            }
        }

        if (phaseMessages.Any())
        {
            var phaseName = $"Current Discussion (Round {phaseMessages.First().Round}-{phaseMessages.Last().Round})";
            phases[phaseName] = phaseMessages;
        }

        return phases;
    }

    private string GetPhaseName(int phaseNumber, int lastRound, int currentRound)
    {
        if (lastRound >= currentRound - 1)
            return "Recent Discussion";

        return _options.PhaseNamingStyle switch
        {
            PhaseNamingStyle.Numbered => $"Phase {phaseNumber}",
            PhaseNamingStyle.Descriptive => phaseNumber switch
            {
                1 => "Initial Positions",
                2 => "Early Deliberation",
                3 => "Middle Discussion",
                _ => $"Phase {phaseNumber}"
            },
            PhaseNamingStyle.RoundBased => $"Rounds {(phaseNumber - 1) * _options.RoundsPerPhase + 1}-{lastRound}",
            _ => $"Phase {phaseNumber}"
        };
    }

    private async Task<string> SummarizePhaseAsync(
        List<DeliberationMessage> messages,
        string phaseName,
        CancellationToken cancellationToken)
    {
        if (LlmProvider == null)
            return CreateFallbackSummary(messages, null);

        var transcript = FormatMessagesForSummary(messages);

        var prompt = $"""
            Summarize this phase of a multi-agent deliberation: "{phaseName}"

            Focus on:
            1. Main topics discussed
            2. Key positions taken by each agent
            3. Any agreements or disagreements that emerged
            4. Decisions or conclusions reached (if any)

            Transcript:
            {transcript}

            Phase summary:
            """;

        var response = await LlmProvider.CompleteAsync(
            [Message.User(prompt)],
            new LlmCompletionOptions { Temperature = 0.3 },
            cancellationToken);

        return response.Content;
    }

    private async Task<string> CreateGlobalSummaryAsync(
        Dictionary<string, HierarchySummaryNode> phases,
        CancellationToken cancellationToken)
    {
        if (LlmProvider == null)
            return $"[Global summary of {phases.Count} phases]";

        var phaseSummaries = string.Join("\n\n", phases.Select(p =>
            $"## {p.Key}\n{p.Value.Summary}"));

        var prompt = $"""
            Create a high-level overview of this multi-agent deliberation based on the phase summaries below.

            The overview should:
            1. Capture the overall arc of the discussion
            2. Highlight the main evolution of positions
            3. Identify the key turning points
            4. Summarize the current state of consensus/disagreement

            Phase summaries:
            {phaseSummaries}

            High-level overview:
            """;

        var response = await LlmProvider.CompleteAsync(
            [Message.User(prompt)],
            new LlmCompletionOptions { Temperature = 0.3 },
            cancellationToken);

        return response.Content;
    }

    private SelectedHierarchyContent SelectFromHierarchy(
        ConversationHierarchy hierarchy,
        int tokenBudget)
    {
        var result = new SelectedHierarchyContent();
        var remainingBudget = tokenBudget;

        var recentPhaseKey = hierarchy.Phases.Keys.LastOrDefault();
        if (recentPhaseKey != null && hierarchy.Phases.TryGetValue(recentPhaseKey, out var recentPhase))
        {
            var recentTokens = EstimateTokens(recentPhase.Messages);
            if (recentTokens <= remainingBudget * _options.RecentPhaseAllocation)
            {
                result.DetailedMessages = recentPhase.Messages;
                remainingBudget -= recentTokens;
            }
        }

        if (hierarchy.Root != null && hierarchy.Root.TokenCount <= remainingBudget * 0.3)
        {
            result.GlobalSummary = hierarchy.Root.Summary;
            remainingBudget -= hierarchy.Root.TokenCount;
        }

        foreach (var phase in hierarchy.Phases.Where(p => p.Key != recentPhaseKey))
        {
            if (phase.Value.TokenCount <= remainingBudget / Math.Max(1, hierarchy.Phases.Count - 1))
            {
                result.PhaseSummaries[phase.Key] = phase.Value.Summary;
                remainingBudget -= phase.Value.TokenCount;
            }
        }

        return result;
    }

    public void ClearCache()
    {
        _hierarchyCache.Clear();
    }
}

public record HierarchicalContextOptions
{
    public int? MaxTokenBudget { get; init; } = 8000;
    public int RoundsPerPhase { get; init; } = 3;
    public double RecentPhaseAllocation { get; init; } = 0.5;
    public PhaseNamingStyle PhaseNamingStyle { get; init; } = PhaseNamingStyle.Descriptive;
    public bool IncludeGlobalSummary { get; init; } = true;
}

public enum PhaseNamingStyle
{
    Numbered,
    Descriptive,
    RoundBased
}

public enum HierarchyLevel
{
    Global,
    Phase,
    Round,
    Message
}

internal class ConversationHierarchy
{
    public HierarchySummaryNode? Root { get; set; }
    public Dictionary<string, HierarchySummaryNode> Phases { get; } = new();
}

internal class HierarchySummaryNode
{
    public HierarchyLevel Level { get; init; }
    public string Summary { get; init; } = string.Empty;
    public List<DeliberationMessage> Messages { get; init; } = [];
    public int TokenCount { get; init; }
}

internal class SelectedHierarchyContent
{
    public string? GlobalSummary { get; set; }
    public Dictionary<string, string> PhaseSummaries { get; } = new();
    public List<DeliberationMessage> DetailedMessages { get; set; } = [];
}
