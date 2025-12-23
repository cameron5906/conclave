using System.Text.Json;
using Conclave.Abstractions;
using Conclave.Models;

namespace Conclave.Voting;

public class ConsensusVotingStrategy : IVotingStrategy
{
    public VotingStrategy StrategyType => VotingStrategy.Consensus;

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
            return FallbackToMajority(responses);
        }

        var synthesizedResponse = await SynthesizeConsensusAsync(
            task, responses, context.ArbiterProvider, cancellationToken);

        var consensusScore = await EvaluateConsensusScoreAsync(
            synthesizedResponse, responses, context.ArbiterProvider, cancellationToken);

        return new VotingResult
        {
            WinningResponse = synthesizedResponse,
            WinningAgentId = "consensus",
            StrategyUsed = VotingStrategy.Consensus,
            VoteTally = responses.ToDictionary(r => r.AgentId, _ => 1),
            ConsensusScore = consensusScore
        };
    }

    private async Task<string> SynthesizeConsensusAsync(
        string task,
        IReadOnlyList<AgentResponse> responses,
        ILlmProvider arbiter,
        CancellationToken cancellationToken)
    {
        var prompt = BuildConsensusPrompt(task, responses);
        var messages = new List<Message> { Message.User(prompt) };

        var options = new LlmCompletionOptions
        {
            SystemPrompt = "You are a consensus builder. Your job is to synthesize multiple perspectives into a unified response that captures the best elements from each while resolving any conflicts.",
            Temperature = 0.3
        };

        var response = await arbiter.CompleteAsync(messages, options, cancellationToken);
        return response.Content;
    }

    private async Task<double> EvaluateConsensusScoreAsync(
        string synthesized,
        IReadOnlyList<AgentResponse> original,
        ILlmProvider arbiter,
        CancellationToken cancellationToken)
    {
        var prompt = $@"Evaluate how well this synthesized response captures the key points from all original responses.

Synthesized response:
{synthesized}

Original responses:
{string.Join("\n\n", original.Select((r, i) => $"Response {i + 1}: {r.Response}"))}

Rate the consensus quality from 0.0 to 1.0, where:
- 1.0 = Perfect synthesis, captures all key points
- 0.5 = Partial synthesis, captures some points
- 0.0 = Failed synthesis, misses key points

Respond with only a number between 0.0 and 1.0.";

        var messages = new List<Message> { Message.User(prompt) };
        var response = await arbiter.CompleteAsync(messages, new LlmCompletionOptions { Temperature = 0 }, cancellationToken);

        if (double.TryParse(response.Content.Trim(), out var score))
        {
            return Math.Clamp(score, 0, 1);
        }

        return 0.5;
    }

    private static string BuildConsensusPrompt(string task, IReadOnlyList<AgentResponse> responses)
    {
        var prompt = $"Original task: {task}\n\n";
        prompt += "The following responses have been provided by different agents:\n\n";

        for (int i = 0; i < responses.Count; i++)
        {
            prompt += $"Agent {responses[i].AgentName}:\n{responses[i].Response}\n\n";
        }

        prompt += "Please synthesize these responses into a single, unified answer that:\n";
        prompt += "1. Captures the key insights from each response\n";
        prompt += "2. Resolves any conflicts by choosing the most well-reasoned position\n";
        prompt += "3. Presents a coherent, comprehensive answer";

        return prompt;
    }

    private static VotingResult FallbackToMajority(IReadOnlyList<AgentResponse> responses)
    {
        var first = responses.First();
        return new VotingResult
        {
            WinningResponse = first.Response,
            WinningStructuredOutput = first.StructuredOutput,
            WinningAgentId = first.AgentId,
            StrategyUsed = VotingStrategy.Consensus,
            VoteTally = new Dictionary<string, int> { [first.AgentId] = 1 },
            ConsensusScore = 1.0 / responses.Count
        };
    }
}
