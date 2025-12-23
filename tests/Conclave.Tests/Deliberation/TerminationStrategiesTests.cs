using FluentAssertions;
using Conclave.Deliberation;
using Xunit;

namespace Conclave.Tests.Deliberation;

public class TerminationStrategiesTests
{
    [Fact]
    public async Task MaxRoundsTermination_TerminatesAtMaxRounds()
    {
        var strategy = new MaxRoundsTermination(3);
        var state = new DeliberationState { Task = "Test", CurrentRound = 3 };

        var decision = await strategy.ShouldTerminateAsync(state);

        decision.ShouldTerminate.Should().BeTrue();
        decision.Reason.Should().Be(TerminationReason.MaxRoundsReached);
    }

    [Fact]
    public async Task MaxRoundsTermination_ContinuesBeforeMaxRounds()
    {
        var strategy = new MaxRoundsTermination(3);
        var state = new DeliberationState { Task = "Test", CurrentRound = 2 };

        var decision = await strategy.ShouldTerminateAsync(state);

        decision.ShouldTerminate.Should().BeFalse();
    }

    [Fact]
    public async Task MaxTokensTermination_TerminatesAtBudget()
    {
        var strategy = new MaxTokensTermination(1000);
        var state = new DeliberationState { Task = "Test", TotalTokensUsed = 1000 };

        var decision = await strategy.ShouldTerminateAsync(state);

        decision.ShouldTerminate.Should().BeTrue();
        decision.Reason.Should().Be(TerminationReason.MaxTokensReached);
    }

    [Fact]
    public async Task MaxTokensTermination_ContinuesUnderBudget()
    {
        var strategy = new MaxTokensTermination(1000);
        var state = new DeliberationState { Task = "Test", TotalTokensUsed = 500 };

        var decision = await strategy.ShouldTerminateAsync(state);

        decision.ShouldTerminate.Should().BeFalse();
    }

    [Fact]
    public async Task MaxTimeTermination_TerminatesAtTimeLimit()
    {
        var strategy = new MaxTimeTermination(TimeSpan.FromMinutes(5));
        var state = new DeliberationState
        {
            Task = "Test",
            ElapsedTime = TimeSpan.FromMinutes(5)
        };

        var decision = await strategy.ShouldTerminateAsync(state);

        decision.ShouldTerminate.Should().BeTrue();
        decision.Reason.Should().Be(TerminationReason.MaxTimeReached);
    }

    [Fact]
    public async Task MaxTimeTermination_ContinuesBeforeTimeLimit()
    {
        var strategy = new MaxTimeTermination(TimeSpan.FromMinutes(5));
        var state = new DeliberationState
        {
            Task = "Test",
            ElapsedTime = TimeSpan.FromMinutes(3)
        };

        var decision = await strategy.ShouldTerminateAsync(state);

        decision.ShouldTerminate.Should().BeFalse();
    }

    [Fact]
    public async Task ConvergenceTermination_TerminatesAtThreshold()
    {
        var strategy = new ConvergenceTermination(0.8, minRounds: 2);
        var state = new DeliberationState
        {
            Task = "Test",
            CurrentRound = 3,
            ConvergenceScore = 0.85
        };

        var decision = await strategy.ShouldTerminateAsync(state);

        decision.ShouldTerminate.Should().BeTrue();
        decision.Reason.Should().Be(TerminationReason.ConvergenceAchieved);
    }

    [Fact]
    public async Task ConvergenceTermination_ContinuesBelowThreshold()
    {
        var strategy = new ConvergenceTermination(0.8, minRounds: 2);
        var state = new DeliberationState
        {
            Task = "Test",
            CurrentRound = 3,
            ConvergenceScore = 0.5
        };

        var decision = await strategy.ShouldTerminateAsync(state);

        decision.ShouldTerminate.Should().BeFalse();
    }

    [Fact]
    public async Task ConvergenceTermination_ContinuesBeforeMinRounds()
    {
        var strategy = new ConvergenceTermination(0.8, minRounds: 3);
        var state = new DeliberationState
        {
            Task = "Test",
            CurrentRound = 2,
            ConvergenceScore = 0.9
        };

        var decision = await strategy.ShouldTerminateAsync(state);

        decision.ShouldTerminate.Should().BeFalse();
    }

    [Fact]
    public async Task CustomTermination_EvaluatesSyncCondition()
    {
        var strategy = new CustomTermination(
            state => state.Transcript.Count >= 5,
            "At least 5 messages");

        var state = new DeliberationState { Task = "Test" };
        for (int i = 0; i < 5; i++)
        {
            state.Transcript.Add(new DeliberationMessage { Content = $"Message {i}" });
        }

        var decision = await strategy.ShouldTerminateAsync(state);

        decision.ShouldTerminate.Should().BeTrue();
        decision.Reason.Should().Be(TerminationReason.CustomCondition);
    }

    [Fact]
    public async Task CustomTermination_EvaluatesAsyncCondition()
    {
        var strategy = new CustomTermination(
            async state =>
            {
                await Task.Delay(1);
                return state.CurrentRound >= 2;
            },
            "Async check");

        var state = new DeliberationState { Task = "Test", CurrentRound = 2 };

        var decision = await strategy.ShouldTerminateAsync(state);

        decision.ShouldTerminate.Should().BeTrue();
    }

    [Fact]
    public async Task CompositeTermination_Any_TerminatesOnFirstMatch()
    {
        var composite = new CompositeTermination(CompositeTermination.CompositeMode.Any)
            .Add(new MaxRoundsTermination(10))
            .Add(new MaxTokensTermination(500));

        var state = new DeliberationState
        {
            Task = "Test",
            CurrentRound = 5,
            TotalTokensUsed = 600
        };

        var decision = await composite.ShouldTerminateAsync(state);

        decision.ShouldTerminate.Should().BeTrue();
        decision.Reason.Should().Be(TerminationReason.MaxTokensReached);
    }

    [Fact]
    public async Task CompositeTermination_Any_ContinuesIfNoMatch()
    {
        var composite = new CompositeTermination(CompositeTermination.CompositeMode.Any)
            .Add(new MaxRoundsTermination(10))
            .Add(new MaxTokensTermination(1000));

        var state = new DeliberationState
        {
            Task = "Test",
            CurrentRound = 5,
            TotalTokensUsed = 500
        };

        var decision = await composite.ShouldTerminateAsync(state);

        decision.ShouldTerminate.Should().BeFalse();
    }

    [Fact]
    public async Task CompositeTermination_All_RequiresAllConditions()
    {
        var composite = new CompositeTermination(CompositeTermination.CompositeMode.All)
            .Add(new MaxRoundsTermination(5))
            .Add(new MaxTokensTermination(500));

        var state = new DeliberationState
        {
            Task = "Test",
            CurrentRound = 5,
            TotalTokensUsed = 600
        };

        var decision = await composite.ShouldTerminateAsync(state);

        decision.ShouldTerminate.Should().BeTrue();
    }

    [Fact]
    public async Task CompositeTermination_All_ContinuesIfNotAllMatch()
    {
        var composite = new CompositeTermination(CompositeTermination.CompositeMode.All)
            .Add(new MaxRoundsTermination(5))
            .Add(new MaxTokensTermination(500));

        var state = new DeliberationState
        {
            Task = "Test",
            CurrentRound = 5,
            TotalTokensUsed = 400
        };

        var decision = await composite.ShouldTerminateAsync(state);

        decision.ShouldTerminate.Should().BeFalse();
    }
}
