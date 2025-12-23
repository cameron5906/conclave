using FluentAssertions;
using Moq;
using Conclave.Abstractions;
using Conclave.Models;
using Conclave.Voting;

namespace Conclave.Tests.Voting;

public class ExpertPanelVotingStrategyTests
{
    private readonly ExpertPanelVotingStrategy _strategy = new();

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
    public async Task EvaluateAsync_WithoutArbiter_FallsBackToWeightedVoting()
    {
        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", Response = "Answer A", Confidence = 0.9 },
            new() { AgentId = "agent2", Response = "Answer B", Confidence = 0.5 }
        };

        var result = await _strategy.EvaluateAsync("task", responses, new VotingContext());

        result.WinningResponse.Should().Be("Answer A");
        result.WinningAgentId.Should().Be("agent1");
        result.StrategyUsed.Should().Be(VotingStrategy.ExpertPanel);
    }

    [Fact]
    public async Task EvaluateAsync_WithoutArbiter_UsesAgentWeights()
    {
        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", Response = "Answer A", Confidence = 0.5 },
            new() { AgentId = "agent2", Response = "Answer B", Confidence = 0.5 }
        };

        var context = new VotingContext
        {
            AgentWeights = new Dictionary<string, double>
            {
                ["agent1"] = 1.0,
                ["agent2"] = 2.0
            }
        };

        var result = await _strategy.EvaluateAsync("task", responses, context);

        result.WinningAgentId.Should().Be("agent2");
    }

    [Fact]
    public async Task EvaluateAsync_WithoutArbiter_UsesDefaultConfidenceWhenMissing()
    {
        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", Response = "Answer A", Confidence = null },
            new() { AgentId = "agent2", Response = "Answer B", Confidence = 0.9 }
        };

        var result = await _strategy.EvaluateAsync("task", responses, new VotingContext());

        result.WinningAgentId.Should().Be("agent2");
    }

    [Fact]
    public async Task EvaluateAsync_WithArbiter_EvaluatesEachResponse()
    {
        var mockProvider = new Mock<ILlmProvider>();
        var callCount = 0;
        mockProvider
            .Setup(p => p.CompleteAsync(
                It.IsAny<IReadOnlyList<Message>>(),
                It.IsAny<LlmCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return new LlmResponse { Content = callCount == 1 ? "0.9,0.8,0.85,0.9,0.8" : "0.5,0.6,0.5,0.5,0.4" };
            });

        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "Answer A" },
            new() { AgentId = "agent2", AgentName = "Agent 2", Response = "Answer B" }
        };

        var context = new VotingContext { ArbiterProvider = mockProvider.Object };
        var result = await _strategy.EvaluateAsync("task", responses, context);

        result.WinningAgentId.Should().Be("agent1");
        result.StrategyUsed.Should().Be(VotingStrategy.ExpertPanel);
    }

    [Fact]
    public async Task EvaluateAsync_WithArbiter_CalculatesAverageScore()
    {
        var mockProvider = new Mock<ILlmProvider>();
        mockProvider
            .Setup(p => p.CompleteAsync(
                It.IsAny<IReadOnlyList<Message>>(),
                It.IsAny<LlmCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse { Content = "0.8,0.8,0.8,0.8,0.8" });

        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "Answer" }
        };

        var context = new VotingContext { ArbiterProvider = mockProvider.Object };
        var result = await _strategy.EvaluateAsync("task", responses, context);

        result.ConsensusScore.Should().BeApproximately(0.8, 0.01);
    }

    [Fact]
    public async Task EvaluateAsync_WithArbiter_HandlesInvalidScoreFormat()
    {
        var mockProvider = new Mock<ILlmProvider>();
        mockProvider
            .Setup(p => p.CompleteAsync(
                It.IsAny<IReadOnlyList<Message>>(),
                It.IsAny<LlmCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse { Content = "invalid response" });

        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "Answer" }
        };

        var context = new VotingContext { ArbiterProvider = mockProvider.Object };
        var result = await _strategy.EvaluateAsync("task", responses, context);

        result.ConsensusScore.Should().Be(0.5);
    }

    [Fact]
    public async Task EvaluateAsync_WithArbiter_FiltersOutOfRangeScores()
    {
        var mockProvider = new Mock<ILlmProvider>();
        mockProvider
            .Setup(p => p.CompleteAsync(
                It.IsAny<IReadOnlyList<Message>>(),
                It.IsAny<LlmCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse { Content = "0.8,1.5,-0.2,0.9,2.0" });

        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "Answer" }
        };

        var context = new VotingContext { ArbiterProvider = mockProvider.Object };
        var result = await _strategy.EvaluateAsync("task", responses, context);

        result.ConsensusScore.Should().BeApproximately(0.85, 0.01);
    }

    [Fact]
    public async Task EvaluateAsync_WithArbiter_NormalizesScoresToVoteTally()
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
                return new LlmResponse { Content = callCount == 1 ? "1.0,1.0,1.0,1.0,1.0" : "0.5,0.5,0.5,0.5,0.5" };
            });

        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "Answer A" },
            new() { AgentId = "agent2", AgentName = "Agent 2", Response = "Answer B" }
        };

        var context = new VotingContext { ArbiterProvider = mockProvider.Object };
        var result = await _strategy.EvaluateAsync("task", responses, context);

        result.VoteTally.Should().ContainKey("agent1");
        result.VoteTally["agent1"].Should().Be(100);
        result.VoteTally["agent2"].Should().Be(50);
    }

    [Fact]
    public async Task EvaluateAsync_PreservesStructuredOutput()
    {
        var structuredData = new { Key = "value" };
        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", Response = "Answer", Confidence = 0.9, StructuredOutput = structuredData }
        };

        var result = await _strategy.EvaluateAsync("task", responses, new VotingContext());

        result.WinningStructuredOutput.Should().Be(structuredData);
    }

    [Fact]
    public async Task EvaluateAsync_WithoutArbiter_CalculatesConsensusScoreAsProportionOfTotal()
    {
        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", Response = "Answer A", Confidence = 0.8 },
            new() { AgentId = "agent2", Response = "Answer B", Confidence = 0.2 }
        };

        var result = await _strategy.EvaluateAsync("task", responses, new VotingContext());

        result.ConsensusScore.Should().Be(0.8);
    }

    [Fact]
    public void StrategyType_ReturnsExpertPanel()
    {
        _strategy.StrategyType.Should().Be(VotingStrategy.ExpertPanel);
    }
}
