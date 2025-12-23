using FluentAssertions;
using Moq;
using Conclave.Abstractions;
using Conclave.Models;
using Conclave.Voting;

namespace Conclave.Tests.Voting;

public class ConsensusVotingStrategyTests
{
    private readonly ConsensusVotingStrategy _strategy = new();

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
    public async Task EvaluateAsync_WithoutArbiter_FallsBackToFirstResponse()
    {
        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "First answer" },
            new() { AgentId = "agent2", AgentName = "Agent 2", Response = "Second answer" }
        };

        var result = await _strategy.EvaluateAsync("task", responses, new VotingContext());

        result.WinningResponse.Should().Be("First answer");
        result.WinningAgentId.Should().Be("agent1");
        result.StrategyUsed.Should().Be(VotingStrategy.Consensus);
    }

    [Fact]
    public async Task EvaluateAsync_WithoutArbiter_CalculatesConsensusScoreBasedOnResponseCount()
    {
        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", Response = "Answer 1" },
            new() { AgentId = "agent2", Response = "Answer 2" },
            new() { AgentId = "agent3", Response = "Answer 3" }
        };

        var result = await _strategy.EvaluateAsync("task", responses, new VotingContext());

        result.ConsensusScore.Should().BeApproximately(1.0 / 3.0, 0.01);
    }

    [Fact]
    public async Task EvaluateAsync_WithArbiter_SynthesizesConsensus()
    {
        var mockProvider = new Mock<ILlmProvider>();
        mockProvider
            .Setup(p => p.CompleteAsync(
                It.IsAny<IReadOnlyList<Message>>(),
                It.IsAny<LlmCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse { Content = "Synthesized consensus response" });

        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "Answer A" },
            new() { AgentId = "agent2", AgentName = "Agent 2", Response = "Answer B" }
        };

        var context = new VotingContext { ArbiterProvider = mockProvider.Object };
        var result = await _strategy.EvaluateAsync("task", responses, context);

        result.WinningResponse.Should().Be("Synthesized consensus response");
        result.WinningAgentId.Should().Be("consensus");
        result.StrategyUsed.Should().Be(VotingStrategy.Consensus);
    }

    [Fact]
    public async Task EvaluateAsync_WithArbiter_EvaluatesConsensusScore()
    {
        var callCount = 0;
        var mockProvider = new Mock<ILlmProvider>();
        mockProvider
            .Setup(p => p.CompleteAsync(
                It.IsAny<IReadOnlyList<Message>>(),
                It.IsAny<LlmCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new LlmResponse { Content = "Synthesized response" }
                    : new LlmResponse { Content = "0.85" };
            });

        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "Answer" }
        };

        var context = new VotingContext { ArbiterProvider = mockProvider.Object };
        var result = await _strategy.EvaluateAsync("task", responses, context);

        result.ConsensusScore.Should().BeApproximately(0.85, 0.01);
    }

    [Fact]
    public async Task EvaluateAsync_WithArbiter_ClampsConsensusScoreToValidRange()
    {
        var callCount = 0;
        var mockProvider = new Mock<ILlmProvider>();
        mockProvider
            .Setup(p => p.CompleteAsync(
                It.IsAny<IReadOnlyList<Message>>(),
                It.IsAny<LlmCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new LlmResponse { Content = "Response" }
                    : new LlmResponse { Content = "1.5" };
            });

        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "Answer" }
        };

        var context = new VotingContext { ArbiterProvider = mockProvider.Object };
        var result = await _strategy.EvaluateAsync("task", responses, context);

        result.ConsensusScore.Should().Be(1.0);
    }

    [Fact]
    public async Task EvaluateAsync_WithArbiter_DefaultsToMidScoreOnParseFailure()
    {
        var callCount = 0;
        var mockProvider = new Mock<ILlmProvider>();
        mockProvider
            .Setup(p => p.CompleteAsync(
                It.IsAny<IReadOnlyList<Message>>(),
                It.IsAny<LlmCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new LlmResponse { Content = "Response" }
                    : new LlmResponse { Content = "Invalid score response" };
            });

        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "Answer" }
        };

        var context = new VotingContext { ArbiterProvider = mockProvider.Object };
        var result = await _strategy.EvaluateAsync("task", responses, context);

        result.ConsensusScore.Should().Be(0.5);
    }

    [Fact]
    public async Task EvaluateAsync_IncludesAllAgentsInVoteTally()
    {
        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "Answer 1" },
            new() { AgentId = "agent2", AgentName = "Agent 2", Response = "Answer 2" },
            new() { AgentId = "agent3", AgentName = "Agent 3", Response = "Answer 3" }
        };

        var result = await _strategy.EvaluateAsync("task", responses, new VotingContext());

        result.VoteTally.Should().ContainKey("agent1");
        result.VoteTally["agent1"].Should().Be(1);
    }

    [Fact]
    public async Task EvaluateAsync_PreservesStructuredOutput()
    {
        var structuredData = new { Key = "value" };
        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", Response = "Answer", StructuredOutput = structuredData }
        };

        var result = await _strategy.EvaluateAsync("task", responses, new VotingContext());

        result.WinningStructuredOutput.Should().Be(structuredData);
    }

    [Fact]
    public void StrategyType_ReturnsConsensus()
    {
        _strategy.StrategyType.Should().Be(VotingStrategy.Consensus);
    }
}
