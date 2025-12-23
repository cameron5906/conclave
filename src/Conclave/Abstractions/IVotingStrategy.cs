using Conclave.Models;

namespace Conclave.Abstractions;

public interface IVotingStrategy
{
    VotingStrategy StrategyType { get; }

    Task<VotingResult> EvaluateAsync(
        string task,
        IReadOnlyList<AgentResponse> responses,
        VotingContext context,
        CancellationToken cancellationToken = default);
}

public record VotingContext
{
    public IReadOnlyDictionary<string, double> AgentWeights { get; init; } = new Dictionary<string, double>();
    public double RequiredConsensusThreshold { get; init; } = 0.6;
    public bool AllowAbstention { get; init; } = false;
    public int MaxRounds { get; init; } = 3;
    public ILlmProvider? ArbiterProvider { get; init; }
}
