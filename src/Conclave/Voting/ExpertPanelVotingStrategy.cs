using Conclave.Abstractions;
using Conclave.Models;

namespace Conclave.Voting;

public class ExpertPanelVotingStrategy : IVotingStrategy
{
    public VotingStrategy StrategyType => VotingStrategy.ExpertPanel;

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

        if (context.ArbiterProvider == null)
        {
            return FallbackToWeighted(responses, context);
        }

        var evaluation = await EvaluateAsExpertPanelAsync(
            task, responses, context, cancellationToken);

        return evaluation;
    }

    private async Task<VotingResult> EvaluateAsExpertPanelAsync(
        string task,
        IReadOnlyList<AgentResponse> responses,
        VotingContext context,
        CancellationToken cancellationToken)
    {
        var scores = new Dictionary<string, double>();

        foreach (var response in responses)
        {
            var score = await EvaluateResponseAsync(
                task, response, context.ArbiterProvider!, cancellationToken);
            scores[response.AgentId] = score;
        }

        var winner = scores.OrderByDescending(s => s.Value).First();
        var winningResponse = responses.First(r => r.AgentId == winner.Key);

        var maxScore = scores.Values.Max();
        var normalizedScores = scores.ToDictionary(
            s => s.Key,
            s => (int)Math.Round(s.Value / maxScore * 100));

        return new VotingResult
        {
            WinningResponse = winningResponse.Response,
            WinningStructuredOutput = winningResponse.StructuredOutput,
            WinningAgentId = winningResponse.AgentId,
            StrategyUsed = VotingStrategy.ExpertPanel,
            VoteTally = normalizedScores,
            ConsensusScore = winner.Value
        };
    }

    private async Task<double> EvaluateResponseAsync(
        string task,
        AgentResponse response,
        ILlmProvider arbiter,
        CancellationToken cancellationToken)
    {
        var prompt = $@"Evaluate this response to the given task on multiple criteria.

Task: {task}

Response from {response.AgentName}:
{response.Response}

Rate this response on each criterion from 0.0 to 1.0:
1. Accuracy - Is the information correct and factual?
2. Completeness - Does it fully address the task?
3. Clarity - Is it well-organized and easy to understand?
4. Relevance - Does it focus on what was asked?
5. Insight - Does it provide valuable perspective?

Respond with 5 numbers separated by commas (e.g., 0.8,0.9,0.7,0.85,0.6)";

        var messages = new List<Message> { Message.User(prompt) };
        var options = new LlmCompletionOptions
        {
            Temperature = 0.1,
            SystemPrompt = "You are an expert evaluator. Provide objective, fair assessments."
        };

        var result = await arbiter.CompleteAsync(messages, options, cancellationToken);
        return ParseScores(result.Content);
    }

    private static double ParseScores(string response)
    {
        try
        {
            var scores = response
                .Split(new[] { ',', ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(s => double.TryParse(s.Trim(), out _))
                .Select(s => double.Parse(s.Trim()))
                .Where(d => d >= 0 && d <= 1)
                .ToList();

            if (scores.Any())
            {
                return scores.Average();
            }
        }
        catch
        {
        }

        return 0.5;
    }

    private static VotingResult FallbackToWeighted(
        IReadOnlyList<AgentResponse> responses,
        VotingContext context)
    {
        var scores = responses.ToDictionary(
            r => r.AgentId,
            r => context.AgentWeights.GetValueOrDefault(r.AgentId, 1.0) * (r.Confidence ?? 0.5));

        var winner = scores.OrderByDescending(s => s.Value).First();
        var winningResponse = responses.First(r => r.AgentId == winner.Key);

        return new VotingResult
        {
            WinningResponse = winningResponse.Response,
            WinningStructuredOutput = winningResponse.StructuredOutput,
            WinningAgentId = winningResponse.AgentId,
            StrategyUsed = VotingStrategy.ExpertPanel,
            VoteTally = scores.ToDictionary(s => s.Key, s => (int)Math.Round(s.Value * 100)),
            ConsensusScore = winner.Value / scores.Values.Sum()
        };
    }
}
