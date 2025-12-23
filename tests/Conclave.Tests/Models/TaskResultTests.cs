using FluentAssertions;
using Conclave.Models;

namespace Conclave.Tests.Models;

public class TaskResultTests
{
    [Fact]
    public void Success_CreatesSuccessfulResult()
    {
        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "Test response" }
        };

        var result = TaskResult<string>.Success("Final answer", responses);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Final answer");
        result.Error.Should().BeNull();
        result.AgentResponses.Should().HaveCount(1);
    }

    [Fact]
    public void Failure_CreatesFailedResult()
    {
        var result = TaskResult<string>.Failure("Something went wrong");

        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
        result.Error.Should().Be("Something went wrong");
        result.AgentResponses.Should().BeEmpty();
    }

    [Fact]
    public void Success_WithVotingResult_IncludesVotingDetails()
    {
        var votingResult = new VotingResult
        {
            WinningResponse = "Winner",
            WinningAgentId = "agent1",
            StrategyUsed = VotingStrategy.Majority,
            ConsensusScore = 0.75
        };

        var result = TaskResult<string>.Success("Winner", [], votingResult);

        result.VotingResult.Should().NotBeNull();
        result.VotingResult!.ConsensusScore.Should().Be(0.75);
        result.VotingResult.StrategyUsed.Should().Be(VotingStrategy.Majority);
    }
}
