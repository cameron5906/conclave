using FluentAssertions;
using Conclave.Abstractions;
using Conclave.Models;
using Conclave.Voting;

namespace Conclave.Tests.Voting;

public class AggregationVotingStrategyTests
{
    private readonly AggregationVotingStrategy _strategy = new();

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
    public async Task EvaluateAsync_WithoutArbiter_CombinesAllResponses()
    {
        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "First point" },
            new() { AgentId = "agent2", AgentName = "Agent 2", Response = "Second point" }
        };

        var result = await _strategy.EvaluateAsync("task", responses, new VotingContext());

        result.WinningResponse.Should().Contain("First point");
        result.WinningResponse.Should().Contain("Second point");
        result.WinningResponse.Should().Contain("[Agent 1]");
        result.WinningResponse.Should().Contain("[Agent 2]");
    }

    [Fact]
    public async Task EvaluateAsync_SetsAggregationAsWinningAgentId()
    {
        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "Response" }
        };

        var result = await _strategy.EvaluateAsync("task", responses, new VotingContext());

        result.WinningAgentId.Should().Be("aggregation");
    }

    [Fact]
    public async Task EvaluateAsync_TalliesAllParticipants()
    {
        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "A" },
            new() { AgentId = "agent2", AgentName = "Agent 2", Response = "B" },
            new() { AgentId = "agent3", AgentName = "Agent 3", Response = "C" }
        };

        var result = await _strategy.EvaluateAsync("task", responses, new VotingContext());

        result.VoteTally.Should().HaveCount(3);
        result.VoteTally["agent1"].Should().Be(1);
        result.VoteTally["agent2"].Should().Be(1);
        result.VoteTally["agent3"].Should().Be(1);
    }

    [Fact]
    public void StrategyType_ReturnsAggregation()
    {
        _strategy.StrategyType.Should().Be(VotingStrategy.Aggregation);
    }
}
