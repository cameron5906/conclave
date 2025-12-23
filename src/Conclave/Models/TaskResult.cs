namespace Conclave.Models;

public class TaskResult<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<AgentResponse> AgentResponses { get; init; } = [];
    public VotingResult? VotingResult { get; init; }
    public TimeSpan ExecutionTime { get; init; }

    public static TaskResult<T> Success(T value, IReadOnlyList<AgentResponse> responses, VotingResult? votingResult = null)
        => new() { IsSuccess = true, Value = value, AgentResponses = responses, VotingResult = votingResult };

    public static TaskResult<T> Failure(string error, IReadOnlyList<AgentResponse>? responses = null)
        => new() { IsSuccess = false, Error = error, AgentResponses = responses ?? [] };
}

public class AgentResponse
{
    public string AgentId { get; init; } = string.Empty;
    public string AgentName { get; init; } = string.Empty;
    public string Response { get; init; } = string.Empty;
    public object? StructuredOutput { get; init; }
    public double? Confidence { get; init; }
    public TimeSpan ResponseTime { get; init; }
    public CompletionUsage? Usage { get; init; }
}

public class VotingResult
{
    public string WinningResponse { get; init; } = string.Empty;
    public object? WinningStructuredOutput { get; init; }
    public string WinningAgentId { get; init; } = string.Empty;
    public VotingStrategy StrategyUsed { get; init; }
    public IReadOnlyDictionary<string, int> VoteTally { get; init; } = new Dictionary<string, int>();
    public double ConsensusScore { get; init; }
}

public enum VotingStrategy
{
    Majority,
    Unanimous,
    Weighted,
    RankedChoice,
    Consensus,
    Aggregation,
    ExpertPanel
}
