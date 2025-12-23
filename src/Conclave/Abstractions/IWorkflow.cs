using Conclave.Models;

namespace Conclave.Abstractions;

public interface IWorkflow<TOutput> where TOutput : class
{
    string Name { get; }
    IReadOnlyList<IAgent> Agents { get; }
    IVotingStrategy VotingStrategy { get; }

    Task<TaskResult<TOutput>> ExecuteAsync(
        string task,
        WorkflowOptions? options = null,
        CancellationToken cancellationToken = default);
}

public class WorkflowOptions
{
    public int MaxRetries { get; init; } = 3;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
    public bool EnableParallelExecution { get; init; } = true;
    public IReadOnlyList<Message>? InitialContext { get; init; }
    public Action<WorkflowProgress>? OnProgress { get; init; }
    public bool RequireConsensus { get; init; } = false;
    public double MinimumConsensusScore { get; init; } = 0.6;
}

public class WorkflowProgress
{
    public WorkflowStage Stage { get; init; }
    public string Message { get; init; } = string.Empty;
    public int CompletedAgents { get; init; }
    public int TotalAgents { get; init; }
    public string? CurrentAgentId { get; init; }
    public double ProgressPercentage => TotalAgents > 0 ? (double)CompletedAgents / TotalAgents * 100 : 0;
}

public enum WorkflowStage
{
    Initializing,
    AgentProcessing,
    Voting,
    ConsensusBuilding,
    Finalizing,
    Completed,
    Failed
}
