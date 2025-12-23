using FluentAssertions;
using Moq;
using Conclave.Abstractions;
using Conclave.Deliberation;
using Xunit;

namespace Conclave.Tests.Deliberation;

public class DeliberationBudgetTests
{
    [Fact]
    public void DeliberationBudget_WithMaxRounds_SetsProperty()
    {
        var budget = new DeliberationBudget()
            .WithMaxRounds(5);

        budget.MaxRounds.Should().Be(5);
    }

    [Fact]
    public void DeliberationBudget_WithMaxTokens_SetsProperty()
    {
        var budget = new DeliberationBudget()
            .WithMaxTokens(10000);

        budget.MaxTokens.Should().Be(10000);
    }

    [Fact]
    public void DeliberationBudget_WithMaxTime_SetsProperty()
    {
        var budget = new DeliberationBudget()
            .WithMaxTime(TimeSpan.FromMinutes(5));

        budget.MaxTime.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void DeliberationBudget_WithConvergenceThreshold_SetsProperty()
    {
        var budget = new DeliberationBudget()
            .WithConvergenceThreshold(0.85);

        budget.ConvergenceThreshold.Should().Be(0.85);
    }

    [Fact]
    public async Task DeliberationBudget_Build_CreatesCompositeTermination()
    {
        var budget = new DeliberationBudget()
            .WithMaxRounds(3)
            .WithMaxTokens(1000);

        var strategy = budget.Build();

        var state = new DeliberationState
        {
            Task = "Test",
            CurrentRound = 3,
            TotalTokensUsed = 500
        };

        var decision = await strategy.ShouldTerminateAsync(state);
        decision.ShouldTerminate.Should().BeTrue();
        decision.Reason.Should().Be(TerminationReason.MaxRoundsReached);
    }

    [Fact]
    public void DeliberationBudget_WithCustomCondition_AddsCondition()
    {
        var budget = new DeliberationBudget()
            .WithCustomCondition(
                state => state.Transcript.Count > 10,
                "Too many messages");

        budget.Should().NotBeNull();
    }

    [Fact]
    public void DeliberationBudget_Fluent_ChainsCorrectly()
    {
        var budget = new DeliberationBudget()
            .WithMaxRounds(5)
            .WithMaxTokens(10000)
            .WithMaxTime(TimeSpan.FromMinutes(3))
            .WithConvergenceThreshold(0.9);

        budget.MaxRounds.Should().Be(5);
        budget.MaxTokens.Should().Be(10000);
        budget.MaxTime.Should().Be(TimeSpan.FromMinutes(3));
        budget.ConvergenceThreshold.Should().Be(0.9);
    }

    [Fact]
    public void DeliberationBudgetBuilder_Fluent_CreatesCorrectBudget()
    {
        var budget = new DeliberationBudgetBuilder()
            .MaxRounds(5)
            .MaxTokens(10000)
            .MaxTime(TimeSpan.FromMinutes(3))
            .ConvergenceThreshold(0.9)
            .Build();

        budget.MaxRounds.Should().Be(5);
        budget.MaxTokens.Should().Be(10000);
        budget.MaxTime.Should().Be(TimeSpan.FromMinutes(3));
        budget.ConvergenceThreshold.Should().Be(0.9);
    }

    [Fact]
    public void DeliberationBudgetBuilder_When_AddsCustomCondition()
    {
        var budget = new DeliberationBudgetBuilder()
            .MaxRounds(10)
            .When(state => state.IsConverged, "Converged")
            .Build();

        budget.Should().NotBeNull();
    }

    [Fact]
    public async Task DeliberationBudgetBuilder_WhenAsync_AddsAsyncCondition()
    {
        var budget = new DeliberationBudgetBuilder()
            .WhenAsync(async state =>
            {
                await Task.Delay(1);
                return state.CurrentRound >= 3;
            }, "Async condition")
            .Build();

        var strategy = budget.Build();
        var state = new DeliberationState { Task = "Test", CurrentRound = 3 };

        var decision = await strategy.ShouldTerminateAsync(state);
        decision.ShouldTerminate.Should().BeTrue();
    }
}
