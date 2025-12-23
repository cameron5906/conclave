using Conclave.Abstractions;
using Conclave.Models;

namespace Conclave.Voting;

public class WeightedVotingStrategy : IVotingStrategy
{
    public VotingStrategy StrategyType => VotingStrategy.Weighted;

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

        var weightedScores = new Dictionary<string, double>();
        var responseMap = new Dictionary<string, AgentResponse>();

        foreach (var response in responses)
        {
            var hash = GetResponseHash(response.Response);
            var weight = context.AgentWeights.GetValueOrDefault(response.AgentId, 1.0);
            var confidence = response.Confidence ?? 1.0;
            var score = weight * confidence;

            weightedScores[hash] = weightedScores.GetValueOrDefault(hash) + score;

            if (!responseMap.ContainsKey(hash))
            {
                responseMap[hash] = response;
            }
        }

        var winner = weightedScores.OrderByDescending(v => v.Value).First();
        var winningResponse = responseMap[winner.Key];
        var totalWeight = responses.Sum(r => context.AgentWeights.GetValueOrDefault(r.AgentId, 1.0));
        var consensusScore = winner.Value / totalWeight;

        return Task.FromResult(new VotingResult
        {
            WinningResponse = winningResponse.Response,
            WinningStructuredOutput = winningResponse.StructuredOutput,
            WinningAgentId = winningResponse.AgentId,
            StrategyUsed = VotingStrategy.Weighted,
            VoteTally = weightedScores.ToDictionary(v => v.Key, v => (int)Math.Round(v.Value * 100)),
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
