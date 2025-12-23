using Conclave.Abstractions;
using Conclave.Models;

namespace Conclave.Voting;

public class MajorityVotingStrategy : IVotingStrategy
{
    public VotingStrategy StrategyType => VotingStrategy.Majority;

    public Task<VotingResult> EvaluateAsync(
        string task,
        IReadOnlyList<AgentResponse> responses,
        VotingContext context,
        CancellationToken cancellationToken = default)
    {
        if (!responses.Any())
        {
            return Task.FromResult(new VotingResult
            {
                WinningResponse = string.Empty,
                ConsensusScore = 0
            });
        }

        var votes = new Dictionary<string, int>();
        var responseMap = responses.ToDictionary(r => r.AgentId, r => r);

        foreach (var response in responses)
        {
            var hash = GetResponseHash(response.Response);
            votes[hash] = votes.GetValueOrDefault(hash) + 1;
        }

        var winner = votes.OrderByDescending(v => v.Value).First();
        var winningResponse = responses.First(r => GetResponseHash(r.Response) == winner.Key);
        var consensusScore = (double)winner.Value / responses.Count;

        return Task.FromResult(new VotingResult
        {
            WinningResponse = winningResponse.Response,
            WinningStructuredOutput = winningResponse.StructuredOutput,
            WinningAgentId = winningResponse.AgentId,
            StrategyUsed = VotingStrategy.Majority,
            VoteTally = votes.ToDictionary(v => v.Key, v => v.Value),
            ConsensusScore = consensusScore
        });
    }

    private static string GetResponseHash(string response)
    {
        var normalized = response.ToLowerInvariant().Trim();
        if (normalized.Length > 100)
        {
            normalized = normalized.Substring(0, 100);
        }
        return normalized.GetHashCode().ToString();
    }
}
