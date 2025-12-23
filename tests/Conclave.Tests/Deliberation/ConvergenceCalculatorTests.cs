using FluentAssertions;
using Conclave.Deliberation;
using Xunit;

namespace Conclave.Tests.Deliberation;

public class ConvergenceCalculatorTests
{
    [Fact]
    public async Task SimpleConvergenceCalculator_ReturnsZero_ForSingleRound()
    {
        var calculator = new SimpleConvergenceCalculator();
        var state = new DeliberationState
        {
            Task = "Test",
            CurrentRound = 1
        };
        state.Transcript.Add(new DeliberationMessage
        {
            AgentId = "agent1",
            Round = 1,
            Content = "Some content"
        });

        var score = await calculator.CalculateConvergenceAsync(state);

        score.Should().Be(0.0);
    }

    [Fact]
    public async Task SimpleConvergenceCalculator_ReturnsZero_ForEmptyTranscript()
    {
        var calculator = new SimpleConvergenceCalculator();
        var state = new DeliberationState
        {
            Task = "Test",
            CurrentRound = 2
        };

        var score = await calculator.CalculateConvergenceAsync(state);

        score.Should().Be(0.0);
    }

    [Fact]
    public async Task SimpleConvergenceCalculator_ReturnsHighScore_ForIdenticalContent()
    {
        var calculator = new SimpleConvergenceCalculator();
        var state = new DeliberationState
        {
            Task = "Test",
            CurrentRound = 2
        };

        state.Transcript.Add(new DeliberationMessage
        {
            AgentId = "agent1",
            Round = 1,
            Content = "The answer is clearly option A because of reason X and reason Y"
        });
        state.Transcript.Add(new DeliberationMessage
        {
            AgentId = "agent1",
            Round = 2,
            Content = "The answer is clearly option A because of reason X and reason Y"
        });

        var score = await calculator.CalculateConvergenceAsync(state);

        score.Should().Be(1.0);
    }

    [Fact]
    public async Task SimpleConvergenceCalculator_ReturnsMediumScore_ForSimilarContent()
    {
        var calculator = new SimpleConvergenceCalculator();
        var state = new DeliberationState
        {
            Task = "Test",
            CurrentRound = 2
        };

        state.Transcript.Add(new DeliberationMessage
        {
            AgentId = "agent1",
            Round = 1,
            Content = "I think the solution should focus on performance optimization"
        });
        state.Transcript.Add(new DeliberationMessage
        {
            AgentId = "agent1",
            Round = 2,
            Content = "The solution should focus on performance and also security"
        });

        var score = await calculator.CalculateConvergenceAsync(state);

        score.Should().BeGreaterThan(0.0);
        score.Should().BeLessThan(1.0);
    }

    [Fact]
    public async Task SimpleConvergenceCalculator_ReturnsLowScore_ForDifferentContent()
    {
        var calculator = new SimpleConvergenceCalculator();
        var state = new DeliberationState
        {
            Task = "Test",
            CurrentRound = 2
        };

        state.Transcript.Add(new DeliberationMessage
        {
            AgentId = "agent1",
            Round = 1,
            Content = "We should use Python for this project"
        });
        state.Transcript.Add(new DeliberationMessage
        {
            AgentId = "agent1",
            Round = 2,
            Content = "Actually Java would be a much better choice here"
        });

        var score = await calculator.CalculateConvergenceAsync(state);

        score.Should().BeLessThan(0.5);
    }

    [Fact]
    public async Task SimpleConvergenceCalculator_AveragesAcrossAgents()
    {
        var calculator = new SimpleConvergenceCalculator();
        var state = new DeliberationState
        {
            Task = "Test",
            CurrentRound = 2
        };

        state.Transcript.Add(new DeliberationMessage
        {
            AgentId = "agent1",
            Round = 1,
            Content = "Option A is the best choice for performance reasons"
        });
        state.Transcript.Add(new DeliberationMessage
        {
            AgentId = "agent2",
            Round = 1,
            Content = "Option B would be better for maintainability"
        });
        state.Transcript.Add(new DeliberationMessage
        {
            AgentId = "agent1",
            Round = 2,
            Content = "Option A is the best choice for performance reasons"
        });
        state.Transcript.Add(new DeliberationMessage
        {
            AgentId = "agent2",
            Round = 2,
            Content = "Option B would be better for maintainability"
        });

        var score = await calculator.CalculateConvergenceAsync(state);

        score.Should().Be(1.0);
    }

    [Fact]
    public async Task SimpleConvergenceCalculator_HandlesNoMatchingPreviousRound()
    {
        var calculator = new SimpleConvergenceCalculator();
        var state = new DeliberationState
        {
            Task = "Test",
            CurrentRound = 2
        };

        state.Transcript.Add(new DeliberationMessage
        {
            AgentId = "agent1",
            Round = 2,
            Content = "Some content"
        });

        var score = await calculator.CalculateConvergenceAsync(state);

        score.Should().Be(0.0);
    }
}
