using FluentAssertions;
using Moq;
using Conclave.Abstractions;
using Conclave.Models;
using Conclave.Voting;

namespace Conclave.Tests.Voting;

public class RankedChoiceVotingStrategyTests
{
    private readonly RankedChoiceVotingStrategy _strategy = new();

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
            new() { AgentId = "agent1", Response = "Only answer", StructuredOutput = new { Data = "test" } }
        };

        var result = await _strategy.EvaluateAsync("task", responses, new VotingContext());

        result.WinningResponse.Should().Be("Only answer");
        result.WinningAgentId.Should().Be("agent1");
        result.StrategyUsed.Should().Be(VotingStrategy.RankedChoice);
        result.ConsensusScore.Should().Be(1.0);
        result.WinningStructuredOutput.Should().NotBeNull();
    }

    [Fact]
    public async Task EvaluateAsync_WithoutArbiter_FallsBackToFirstResponse()
    {
        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", Response = "First answer" },
            new() { AgentId = "agent2", Response = "Second answer" }
        };

        var result = await _strategy.EvaluateAsync("task", responses, new VotingContext());

        result.WinningResponse.Should().Be("First answer");
        result.WinningAgentId.Should().Be("agent1");
        result.StrategyUsed.Should().Be(VotingStrategy.RankedChoice);
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
    public async Task EvaluateAsync_WithArbiter_UsesRanking()
    {
        var mockProvider = new Mock<ILlmProvider>();
        mockProvider
            .Setup(p => p.CompleteAsync(
                It.IsAny<IReadOnlyList<Message>>(),
                It.IsAny<LlmCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse { Content = "2,1,3" });

        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "Answer A" },
            new() { AgentId = "agent2", AgentName = "Agent 2", Response = "Answer B" },
            new() { AgentId = "agent3", AgentName = "Agent 3", Response = "Answer C" }
        };

        var context = new VotingContext { ArbiterProvider = mockProvider.Object };
        var result = await _strategy.EvaluateAsync("task", responses, context);

        result.WinningResponse.Should().Be("Answer B");
        result.WinningAgentId.Should().Be("agent2");
    }

    [Fact]
    public async Task EvaluateAsync_WithArbiter_HandlesInvalidRankingFormat()
    {
        var mockProvider = new Mock<ILlmProvider>();
        mockProvider
            .Setup(p => p.CompleteAsync(
                It.IsAny<IReadOnlyList<Message>>(),
                It.IsAny<LlmCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse { Content = "invalid ranking response" });

        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "Answer A" },
            new() { AgentId = "agent2", AgentName = "Agent 2", Response = "Answer B" }
        };

        var context = new VotingContext { ArbiterProvider = mockProvider.Object };
        var result = await _strategy.EvaluateAsync("task", responses, context);

        result.WinningResponse.Should().Be("Answer A");
    }

    [Fact]
    public async Task EvaluateAsync_WithArbiter_HandlesPartialRanking()
    {
        var mockProvider = new Mock<ILlmProvider>();
        mockProvider
            .Setup(p => p.CompleteAsync(
                It.IsAny<IReadOnlyList<Message>>(),
                It.IsAny<LlmCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse { Content = "1" });

        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "Answer A" },
            new() { AgentId = "agent2", AgentName = "Agent 2", Response = "Answer B" }
        };

        var context = new VotingContext { ArbiterProvider = mockProvider.Object };
        var result = await _strategy.EvaluateAsync("task", responses, context);

        result.WinningResponse.Should().Be("Answer A");
    }

    [Fact]
    public async Task EvaluateAsync_WithArbiter_HandlesOutOfRangeNumbers()
    {
        var mockProvider = new Mock<ILlmProvider>();
        mockProvider
            .Setup(p => p.CompleteAsync(
                It.IsAny<IReadOnlyList<Message>>(),
                It.IsAny<LlmCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse { Content = "5, 10, 1" });

        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "Answer A" },
            new() { AgentId = "agent2", AgentName = "Agent 2", Response = "Answer B" }
        };

        var context = new VotingContext { ArbiterProvider = mockProvider.Object };
        var result = await _strategy.EvaluateAsync("task", responses, context);

        result.WinningResponse.Should().Be("Answer A");
    }

    [Fact]
    public async Task EvaluateAsync_WithArbiter_RunsMultipleElectionRounds()
    {
        var mockProvider = new Mock<ILlmProvider>();
        mockProvider
            .Setup(p => p.CompleteAsync(
                It.IsAny<IReadOnlyList<Message>>(),
                It.IsAny<LlmCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse { Content = "1,2,3" });

        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "Answer A" },
            new() { AgentId = "agent2", AgentName = "Agent 2", Response = "Answer B" },
            new() { AgentId = "agent3", AgentName = "Agent 3", Response = "Answer C" }
        };

        var context = new VotingContext { ArbiterProvider = mockProvider.Object };
        var result = await _strategy.EvaluateAsync("task", responses, context);

        result.StrategyUsed.Should().Be(VotingStrategy.RankedChoice);
        result.VoteTally.Should().NotBeEmpty();
    }

    [Fact]
    public async Task EvaluateAsync_IncludesVoteTallyForRemainingCandidates()
    {
        var mockProvider = new Mock<ILlmProvider>();
        mockProvider
            .Setup(p => p.CompleteAsync(
                It.IsAny<IReadOnlyList<Message>>(),
                It.IsAny<LlmCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse { Content = "1,2" });

        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "Answer A" },
            new() { AgentId = "agent2", AgentName = "Agent 2", Response = "Answer B" }
        };

        var context = new VotingContext { ArbiterProvider = mockProvider.Object };
        var result = await _strategy.EvaluateAsync("task", responses, context);

        result.VoteTally.Should().ContainKey("agent1");
    }

    [Fact]
    public async Task EvaluateAsync_PreservesStructuredOutput()
    {
        var mockProvider = new Mock<ILlmProvider>();
        mockProvider
            .Setup(p => p.CompleteAsync(
                It.IsAny<IReadOnlyList<Message>>(),
                It.IsAny<LlmCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse { Content = "2,1" });

        var structuredData = new { Key = "value" };
        var responses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "Answer A" },
            new() { AgentId = "agent2", AgentName = "Agent 2", Response = "Answer B", StructuredOutput = structuredData }
        };

        var context = new VotingContext { ArbiterProvider = mockProvider.Object };
        var result = await _strategy.EvaluateAsync("task", responses, context);

        result.WinningStructuredOutput.Should().Be(structuredData);
    }

    [Fact]
    public void StrategyType_ReturnsRankedChoice()
    {
        _strategy.StrategyType.Should().Be(VotingStrategy.RankedChoice);
    }
}
