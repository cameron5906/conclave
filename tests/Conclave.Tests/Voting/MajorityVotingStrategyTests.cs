using FluentAssertions;
using Conclave.Abstractions;
using Conclave.Models;
using Conclave.Voting;

namespace Conclave.Tests.Voting;

public class MajorityVotingStrategyTests
{
    private readonly MajorityVotingStrategy _strategy = new();

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
    public async Task EvaluateAsync_WithSingleResponse_ReturnsThatResponse()
    {
        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", Response = "Only answer" }
        };

        var result = await _strategy.EvaluateAsync("task", responses, new VotingContext());

        result.WinningResponse.Should().Be("Only answer");
        result.ConsensusScore.Should().Be(1.0);
    }

    [Fact]
    public async Task EvaluateAsync_WithMajorityAgreement_ReturnsWinner()
    {
        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", Response = "Answer A" },
            new() { AgentId = "agent2", Response = "Answer A" },
            new() { AgentId = "agent3", Response = "Answer B" }
        };

        var result = await _strategy.EvaluateAsync("task", responses, new VotingContext());

        result.WinningResponse.Should().Be("Answer A");
        result.ConsensusScore.Should().BeApproximately(0.66, 0.01);
    }

    [Fact]
    public async Task EvaluateAsync_SetsCorrectStrategyType()
    {
        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", Response = "Answer" }
        };

        var result = await _strategy.EvaluateAsync("task", responses, new VotingContext());

        result.StrategyUsed.Should().Be(VotingStrategy.Majority);
    }

    [Fact]
    public void StrategyType_ReturnsMajority()
    {
        _strategy.StrategyType.Should().Be(VotingStrategy.Majority);
    }
}
