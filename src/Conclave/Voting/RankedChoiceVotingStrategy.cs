using Conclave.Abstractions;
using Conclave.Models;

namespace Conclave.Voting;

public class RankedChoiceVotingStrategy : IVotingStrategy
{
    public VotingStrategy StrategyType => VotingStrategy.RankedChoice;

    public async Task<VotingResult> EvaluateAsync(
        string task,
        IReadOnlyList<AgentResponse> responses,
        VotingContext context,
        CancellationToken cancellationToken = default)
    {
        if (!responses.Any())
        {
            return new VotingResult
            {
                WinningResponse = string.Empty,
                ConsensusScore = 0
            };
        }

        if (responses.Count == 1)
        {
            return new VotingResult
            {
                WinningResponse = responses[0].Response,
                WinningStructuredOutput = responses[0].StructuredOutput,
                WinningAgentId = responses[0].AgentId,
                StrategyUsed = VotingStrategy.RankedChoice,
                VoteTally = new Dictionary<string, int> { [responses[0].AgentId] = 1 },
                ConsensusScore = 1.0
            };
        }

        if (context.ArbiterProvider == null)
        {
            return FallbackToFirst(responses);
        }

        var rankings = await GetRankingsFromArbiterAsync(
            task, responses, context.ArbiterProvider, cancellationToken);

        var result = RunRankedChoiceElection(responses, rankings);
        return result;
    }

    private async Task<List<List<int>>> GetRankingsFromArbiterAsync(
        string task,
        IReadOnlyList<AgentResponse> responses,
        ILlmProvider arbiter,
        CancellationToken cancellationToken)
    {
        var prompt = BuildRankingPrompt(task, responses);
        var messages = new List<Message> { Message.User(prompt) };

        var options = new LlmCompletionOptions
        {
            SystemPrompt = "You are an impartial judge evaluating multiple responses. Rank them from best to worst.",
            Temperature = 0.1
        };

        var response = await arbiter.CompleteAsync(messages, options, cancellationToken);
        return ParseRankings(response.Content, responses.Count);
    }

    private static string BuildRankingPrompt(string task, IReadOnlyList<AgentResponse> responses)
    {
        var prompt = $"Task: {task}\n\n";
        prompt += "Please rank the following responses from best (1) to worst:\n\n";

        for (int i = 0; i < responses.Count; i++)
        {
            prompt += $"Response {i + 1} (from {responses[i].AgentName}):\n{responses[i].Response}\n\n";
        }

        prompt += "Provide your ranking as a comma-separated list of numbers, e.g., '2,1,3' means Response 2 is best, Response 1 is second, Response 3 is worst.";

        return prompt;
    }

    private static List<List<int>> ParseRankings(string response, int count)
    {
        var rankings = new List<List<int>>();

        try
        {
            var numbers = response
                .Split(new[] { ',', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(s => int.TryParse(s.Trim(), out _))
                .Select(s => int.Parse(s.Trim()) - 1)
                .Where(n => n >= 0 && n < count)
                .Distinct()
                .ToList();

            if (numbers.Count > 0)
            {
                rankings.Add(numbers);
            }
        }
        catch
        {
        }

        if (!rankings.Any())
        {
            rankings.Add(Enumerable.Range(0, count).ToList());
        }

        return rankings;
    }

    private static VotingResult RunRankedChoiceElection(
        IReadOnlyList<AgentResponse> responses,
        List<List<int>> rankings)
    {
        var eliminated = new HashSet<int>();
        var voteCount = new Dictionary<int, int>();

        while (eliminated.Count < responses.Count - 1)
        {
            voteCount.Clear();
            for (int i = 0; i < responses.Count; i++)
            {
                if (!eliminated.Contains(i))
                {
                    voteCount[i] = 0;
                }
            }

            foreach (var ranking in rankings)
            {
                var topChoice = ranking.FirstOrDefault(r => !eliminated.Contains(r));
                if (voteCount.ContainsKey(topChoice))
                {
                    voteCount[topChoice]++;
                }
            }

            var totalVotes = voteCount.Values.Sum();
            var winner = voteCount.OrderByDescending(v => v.Value).First();

            if (winner.Value > totalVotes / 2.0)
            {
                var winningResponse = responses[winner.Key];
                return new VotingResult
                {
                    WinningResponse = winningResponse.Response,
                    WinningStructuredOutput = winningResponse.StructuredOutput,
                    WinningAgentId = winningResponse.AgentId,
                    StrategyUsed = VotingStrategy.RankedChoice,
                    VoteTally = voteCount.ToDictionary(v => responses[v.Key].AgentId, v => v.Value),
                    ConsensusScore = (double)winner.Value / totalVotes
                };
            }

            var loser = voteCount.OrderBy(v => v.Value).First().Key;
            eliminated.Add(loser);
        }

        var finalWinner = voteCount.OrderByDescending(v => v.Value).First().Key;
        var final = responses[finalWinner];

        return new VotingResult
        {
            WinningResponse = final.Response,
            WinningStructuredOutput = final.StructuredOutput,
            WinningAgentId = final.AgentId,
            StrategyUsed = VotingStrategy.RankedChoice,
            VoteTally = voteCount.ToDictionary(v => responses[v.Key].AgentId, v => v.Value),
            ConsensusScore = 1.0 / responses.Count
        };
    }

    private static VotingResult FallbackToFirst(IReadOnlyList<AgentResponse> responses)
    {
        var first = responses.First();
        return new VotingResult
        {
            WinningResponse = first.Response,
            WinningStructuredOutput = first.StructuredOutput,
            WinningAgentId = first.AgentId,
            StrategyUsed = VotingStrategy.RankedChoice,
            VoteTally = new Dictionary<string, int> { [first.AgentId] = 1 },
            ConsensusScore = 1.0 / responses.Count
        };
    }
}
