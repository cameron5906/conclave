namespace Conclave.Deliberation;

public enum DeliberationMode
{
    RoundRobin,
    Debate,
    Moderated,
    FreeForm
}

public class DeliberationMessage
{
    public string AgentId { get; init; } = string.Empty;
    public string AgentName { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public int Round { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? InResponseTo { get; init; }
    public int TokenCount { get; init; }
}

public class DeliberationState
{
    public string Task { get; init; } = string.Empty;
    public int CurrentRound { get; set; }
    public int TotalTokensUsed { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public List<DeliberationMessage> Transcript { get; } = new();
    public Dictionary<string, List<string>> AgentPositions { get; } = new();
    public double? ConvergenceScore { get; set; }
    public bool IsConverged { get; set; }
    public string? CurrentSpeaker { get; set; }
    public List<string> ParticipatingAgentIds { get; init; } = new();

    public IReadOnlyList<DeliberationMessage> GetMessagesForAgent(string agentId)
    {
        return Transcript.Where(m => m.AgentId != agentId).ToList();
    }

    public IReadOnlyList<DeliberationMessage> GetLastRoundMessages()
    {
        return Transcript.Where(m => m.Round == CurrentRound - 1).ToList();
    }

    public string GetFormattedTranscript()
    {
        return string.Join("\n\n", Transcript.Select(m =>
            $"[Round {m.Round}] {m.AgentName}:\n{m.Content}"));
    }
}

public class DeliberationResult<TOutput> where TOutput : class
{
    public bool IsSuccess { get; init; }
    public TOutput? Value { get; init; }
    public string? Error { get; init; }
    public DeliberationState State { get; init; } = new();
    public TerminationReason TerminationReason { get; init; }
    public int TotalRounds { get; init; }
    public int TotalTokens { get; init; }
    public TimeSpan TotalTime { get; init; }
    public double FinalConvergenceScore { get; init; }

    public static DeliberationResult<TOutput> Success(
        TOutput value,
        DeliberationState state,
        TerminationReason reason) => new()
    {
        IsSuccess = true,
        Value = value,
        State = state,
        TerminationReason = reason,
        TotalRounds = state.CurrentRound,
        TotalTokens = state.TotalTokensUsed,
        TotalTime = state.ElapsedTime,
        FinalConvergenceScore = state.ConvergenceScore ?? 0
    };

    public static DeliberationResult<TOutput> Failure(string error, DeliberationState state) => new()
    {
        IsSuccess = false,
        Error = error,
        State = state,
        TerminationReason = TerminationReason.Error,
        TotalRounds = state.CurrentRound,
        TotalTokens = state.TotalTokensUsed,
        TotalTime = state.ElapsedTime
    };
}

public enum TerminationReason
{
    MaxRoundsReached,
    MaxTokensReached,
    MaxTimeReached,
    ConvergenceAchieved,
    AgentDecision,
    WorkflowDecision,
    CustomCondition,
    ManualStop,
    Error
}

public class DeliberationProgress
{
    public int CurrentRound { get; init; }
    public int? MaxRounds { get; init; }
    public string CurrentSpeaker { get; init; } = string.Empty;
    public int TokensUsed { get; init; }
    public int? TokenBudget { get; init; }
    public TimeSpan ElapsedTime { get; init; }
    public TimeSpan? TimeBudget { get; init; }
    public double? ConvergenceScore { get; init; }
    public double? ConvergenceThreshold { get; init; }
    public string Message { get; init; } = string.Empty;
    public DeliberationStage Stage { get; init; }
}

public enum DeliberationStage
{
    Initializing,
    RoundStarting,
    AgentSpeaking,
    RoundComplete,
    EvaluatingConvergence,
    CheckingTermination,
    Synthesizing,
    Complete,
    Failed
}
