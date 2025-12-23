using FluentAssertions;
using Conclave.Abstractions;
using Conclave.Models;
using Conclave.Voting;

namespace Conclave.Tests.Voting;

public class WeightedVotingStrategyTests
{
    private readonly WeightedVotingStrategy _strategy = new();

    [Fact]
    public async Task EvaluateAsync_WithEmptyResponses_ReturnsEmptyResult()
    {
        var result = await _strategy.EvaluateAsync(
            "test task",
            Array.Empty<AgentResponse>(),
            new VotingContext());

        result.WinningResponse.Should().BeEmpty();
        result.ConsensusScore.Should().Be(0);
    }

    [Fact]
    public async Task EvaluateAsync_WithWeights_RespectsAgentWeights()
    {
        var responses = new List<AgentResponse>
        {
            new() { AgentId = "expert", Response = "Expert answer" },
            new() { AgentId = "novice1", Response = "Novice answer" },
            new() { AgentId = "novice2", Response = "Novice answer" }
        };

        var context = new VotingContext
        {
            AgentWeights = new Dictionary<string, double>
            {
                ["expert"] = 3.0,
                ["novice1"] = 1.0,
                ["novice2"] = 1.0
            }
        };

        var result = await _strategy.EvaluateAsync("task", responses, context);

        result.WinningResponse.Should().Be("Expert answer");
    }

    [Fact]
    public async Task EvaluateAsync_WithConfidence_ConsidersConfidenceScore()
    {
        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", Response = "Answer A", Confidence = 0.9 },
            new() { AgentId = "agent2", Response = "Answer B", Confidence = 0.3 }
        };

        var result = await _strategy.EvaluateAsync("task", responses, new VotingContext());

        result.WinningResponse.Should().Be("Answer A");
    }

    [Fact]
    public async Task EvaluateAsync_WithoutWeights_UsesDefaultWeight()
    {
        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", Response = "Answer A" },
            new() { AgentId = "agent2", Response = "Answer A" },
            new() { AgentId = "agent3", Response = "Answer B" }
        };

        var result = await _strategy.EvaluateAsync("task", responses, new VotingContext());

        result.WinningResponse.Should().Be("Answer A");
    }

    [Fact]
    public void StrategyType_ReturnsWeighted()
    {
        _strategy.StrategyType.Should().Be(VotingStrategy.Weighted);
    }
}
