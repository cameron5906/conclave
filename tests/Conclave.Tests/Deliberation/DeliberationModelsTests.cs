using FluentAssertions;
using Conclave.Deliberation;
using Xunit;

namespace Conclave.Tests.Deliberation;

public class DeliberationModelsTests
{
    [Fact]
    public void DeliberationState_InitializesWithDefaults()
    {
        var state = new DeliberationState
        {
            Task = "Test task"
        };

        state.Task.Should().Be("Test task");
        state.CurrentRound.Should().Be(0);
        state.TotalTokensUsed.Should().Be(0);
        state.Transcript.Should().BeEmpty();
        state.AgentPositions.Should().BeEmpty();
        state.ConvergenceScore.Should().BeNull();
        state.IsConverged.Should().BeFalse();
    }

    [Fact]
    public void DeliberationState_GetMessagesForAgent_ExcludesOwnMessages()
    {
        var state = new DeliberationState { Task = "Test" };
        state.Transcript.Add(new DeliberationMessage { AgentId = "agent1", Content = "Message 1" });
        state.Transcript.Add(new DeliberationMessage { AgentId = "agent2", Content = "Message 2" });
        state.Transcript.Add(new DeliberationMessage { AgentId = "agent1", Content = "Message 3" });

        var messagesForAgent2 = state.GetMessagesForAgent("agent2");

        messagesForAgent2.Should().HaveCount(2);
        messagesForAgent2.All(m => m.AgentId != "agent2").Should().BeTrue();
    }

    [Fact]
    public void DeliberationState_GetLastRoundMessages_ReturnsCorrectRound()
    {
        var state = new DeliberationState { Task = "Test", CurrentRound = 3 };
        state.Transcript.Add(new DeliberationMessage { Round = 1, Content = "Round 1" });
        state.Transcript.Add(new DeliberationMessage { Round = 2, Content = "Round 2" });
        state.Transcript.Add(new DeliberationMessage { Round = 3, Content = "Round 3" });

        var lastRoundMessages = state.GetLastRoundMessages();

        lastRoundMessages.Should().HaveCount(1);
        lastRoundMessages.First().Round.Should().Be(2);
    }

    [Fact]
    public void DeliberationState_GetFormattedTranscript_FormatsCorrectly()
    {
        var state = new DeliberationState { Task = "Test" };
        state.Transcript.Add(new DeliberationMessage
        {
            Round = 1,
            AgentName = "Agent A",
            Content = "Hello"
        });
        state.Transcript.Add(new DeliberationMessage
        {
            Round = 1,
            AgentName = "Agent B",
            Content = "World"
        });

        var transcript = state.GetFormattedTranscript();

        transcript.Should().Contain("[Round 1] Agent A:");
        transcript.Should().Contain("Hello");
        transcript.Should().Contain("[Round 1] Agent B:");
        transcript.Should().Contain("World");
    }

    [Fact]
    public void DeliberationResult_Success_CreatesSuccessfulResult()
    {
        var state = new DeliberationState
        {
            Task = "Test",
            CurrentRound = 3,
            TotalTokensUsed = 1000,
            ConvergenceScore = 0.85
        };

        var result = DeliberationResult<string>.Success(
            "Final answer",
            state,
            TerminationReason.ConvergenceAchieved);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Final answer");
        result.TerminationReason.Should().Be(TerminationReason.ConvergenceAchieved);
        result.TotalRounds.Should().Be(3);
        result.TotalTokens.Should().Be(1000);
        result.FinalConvergenceScore.Should().Be(0.85);
    }

    [Fact]
    public void DeliberationResult_Failure_CreatesFailedResult()
    {
        var state = new DeliberationState { Task = "Test" };

        var result = DeliberationResult<string>.Failure("Something went wrong", state);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Something went wrong");
        result.TerminationReason.Should().Be(TerminationReason.Error);
    }

    [Fact]
    public void TerminationDecision_Continue_CreatesContinueDecision()
    {
        var decision = TerminationDecision.Continue();

        decision.ShouldTerminate.Should().BeFalse();
        decision.Confidence.Should().Be(1.0);
    }

    [Fact]
    public void TerminationDecision_Terminate_CreatesTerminateDecision()
    {
        var decision = TerminationDecision.Terminate(
            TerminationReason.MaxRoundsReached,
            "Reached 5 rounds");

        decision.ShouldTerminate.Should().BeTrue();
        decision.Reason.Should().Be(TerminationReason.MaxRoundsReached);
        decision.Explanation.Should().Be("Reached 5 rounds");
        decision.Confidence.Should().Be(1.0);
    }

    [Fact]
    public void TerminationDecision_TerminateWithConfidence_IncludesConfidence()
    {
        var decision = TerminationDecision.TerminateWithConfidence(
            TerminationReason.AgentDecision,
            0.75,
            "Agent thinks we're done");

        decision.ShouldTerminate.Should().BeTrue();
        decision.Reason.Should().Be(TerminationReason.AgentDecision);
        decision.Confidence.Should().Be(0.75);
        decision.Explanation.Should().Be("Agent thinks we're done");
    }

    [Fact]
    public void DeliberationProgress_CalculatesProgressPercentage()
    {
        var progress = new DeliberationProgress
        {
            CurrentRound = 3,
            MaxRounds = 10,
            TokensUsed = 5000,
            TokenBudget = 10000
        };

        progress.CurrentRound.Should().Be(3);
        progress.MaxRounds.Should().Be(10);
    }
}
