using Conclave.Abstractions;
using Conclave.Models;

namespace Conclave.Voting;

public class AggregationVotingStrategy : IVotingStrategy
{
    public VotingStrategy StrategyType => VotingStrategy.Aggregation;

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
            return SimpleAggregation(responses);
        }

        var aggregatedResponse = await AggregateWithLlmAsync(
            task, responses, context.ArbiterProvider, cancellationToken);

        return new VotingResult
        {
            WinningResponse = aggregatedResponse,
            WinningAgentId = "aggregation",
            StrategyUsed = VotingStrategy.Aggregation,
            VoteTally = responses.ToDictionary(r => r.AgentId, _ => 1),
            ConsensusScore = 1.0
        };
    }

    private async Task<string> AggregateWithLlmAsync(
        string task,
        IReadOnlyList<AgentResponse> responses,
        ILlmProvider arbiter,
        CancellationToken cancellationToken)
    {
        var prompt = BuildAggregationPrompt(task, responses);
        var messages = new List<Message> { Message.User(prompt) };

        var options = new LlmCompletionOptions
        {
            SystemPrompt = @"You are a response aggregator. Your task is to combine multiple agent responses into a single comprehensive answer.

Guidelines:
1. Include all unique points from each response
2. Remove redundant information
3. Organize the combined response logically
4. Preserve important nuances and caveats
5. Present the information clearly and coherently",
            Temperature = 0.3
        };

        var response = await arbiter.CompleteAsync(messages, options, cancellationToken);
        return response.Content;
    }

    private static string BuildAggregationPrompt(string task, IReadOnlyList<AgentResponse> responses)
    {
        var prompt = $"Original task: {task}\n\n";
        prompt += "Please aggregate the following responses into a single comprehensive answer:\n\n";

        for (int i = 0; i < responses.Count; i++)
        {
            prompt += $"--- Response from {responses[i].AgentName} ---\n{responses[i].Response}\n\n";
        }

        prompt += "Create a unified response that combines all unique insights while eliminating redundancy.";

        return prompt;
    }

    private static VotingResult SimpleAggregation(IReadOnlyList<AgentResponse> responses)
    {
        var combined = string.Join("\n\n---\n\n",
            responses.Select(r => $"[{r.AgentName}]: {r.Response}"));

        return new VotingResult
        {
            WinningResponse = combined,
            WinningAgentId = "aggregation",
            StrategyUsed = VotingStrategy.Aggregation,
            VoteTally = responses.ToDictionary(r => r.AgentId, _ => 1),
            ConsensusScore = 1.0
        };
    }
}
