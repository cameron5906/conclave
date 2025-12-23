using FluentAssertions;
using Moq;
using Conclave.Abstractions;
using Conclave.Deliberation;
using Conclave.Models;
using Xunit;
using DeliberationFactory = Conclave.Deliberation.Deliberation;

namespace Conclave.Tests.Deliberation;

public class DeliberationBuilderTests
{
    private Mock<IAgent> CreateMockAgent(string id, string name)
    {
        var agent = new Mock<IAgent>();
        agent.Setup(a => a.Id).Returns(id);
        agent.Setup(a => a.Name).Returns(name);
        agent.Setup(a => a.Personality).Returns(AgentPersonality.Default);
        return agent;
    }

    [Fact]
    public void DeliberationBuilder_RequiresAtLeastOneAgent()
    {
        var builder = DeliberationFactory.Create();

        var action = () => builder.Build();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least one agent*");
    }

    [Fact]
    public void DeliberationBuilder_AddAgent_AddsAgent()
    {
        var agent = CreateMockAgent("agent1", "Agent 1");

        var workflow = DeliberationFactory.Create()
            .AddAgent(agent.Object)
            .WithMaxRounds(3)
            .Build();

        workflow.Agents.Should().HaveCount(1);
    }

    [Fact]
    public void DeliberationBuilder_AddAgents_AddsMultipleAgents()
    {
        var agents = new[]
        {
            CreateMockAgent("agent1", "Agent 1").Object,
            CreateMockAgent("agent2", "Agent 2").Object,
            CreateMockAgent("agent3", "Agent 3").Object
        };

        var workflow = DeliberationFactory.Create()
            .AddAgents(agents)
            .WithMaxRounds(3)
            .Build();

        workflow.Agents.Should().HaveCount(3);
    }

    [Fact]
    public void DeliberationBuilder_WithName_SetsName()
    {
        var agent = CreateMockAgent("agent1", "Agent 1");

        var workflow = DeliberationFactory.Create()
            .WithName("Test Deliberation")
            .AddAgent(agent.Object)
            .WithMaxRounds(3)
            .Build();

        workflow.Name.Should().Be("Test Deliberation");
    }

    [Fact]
    public void DeliberationBuilder_WithRoundRobin_SetsMode()
    {
        var agent = CreateMockAgent("agent1", "Agent 1");

        var workflow = DeliberationFactory.Create()
            .AddAgent(agent.Object)
            .WithRoundRobin()
            .WithMaxRounds(3)
            .Build();

        workflow.Mode.Should().Be(DeliberationMode.RoundRobin);
    }

    [Fact]
    public void DeliberationBuilder_WithDebate_SetsMode()
    {
        var agent = CreateMockAgent("agent1", "Agent 1");

        var workflow = DeliberationFactory.Create()
            .AddAgent(agent.Object)
            .WithDebate()
            .WithMaxRounds(3)
            .Build();

        workflow.Mode.Should().Be(DeliberationMode.Debate);
    }

    [Fact]
    public void DeliberationBuilder_WithModeration_SetsModeAndModerator()
    {
        var agent = CreateMockAgent("agent1", "Agent 1");
        var moderator = CreateMockAgent("moderator", "Moderator");

        var workflow = DeliberationFactory.Create()
            .AddAgent(agent.Object)
            .WithModeration(moderator.Object)
            .WithMaxRounds(3)
            .Build();

        workflow.Mode.Should().Be(DeliberationMode.Moderated);
    }

    [Fact]
    public void DeliberationBuilder_WithFreeForm_SetsMode()
    {
        var agent = CreateMockAgent("agent1", "Agent 1");

        var workflow = DeliberationFactory.Create()
            .AddAgent(agent.Object)
            .WithFreeForm()
            .WithMaxRounds(3)
            .Build();

        workflow.Mode.Should().Be(DeliberationMode.FreeForm);
    }

    [Fact]
    public void DeliberationBuilder_WithBudget_ConfiguresBudget()
    {
        var agent = CreateMockAgent("agent1", "Agent 1");

        var workflow = DeliberationFactory.Create()
            .AddAgent(agent.Object)
            .WithBudget(budget => budget
                .WithMaxRounds(5)
                .WithMaxTokens(10000)
                .WithConvergenceThreshold(0.9))
            .Build();

        workflow.Should().NotBeNull();
    }

    [Fact]
    public void DeliberationBuilder_WithMaxRounds_ConfiguresBudget()
    {
        var agent = CreateMockAgent("agent1", "Agent 1");

        var workflow = DeliberationFactory.Create()
            .AddAgent(agent.Object)
            .WithMaxRounds(7)
            .Build();

        workflow.Should().NotBeNull();
    }

    [Fact]
    public void DeliberationBuilder_TerminateWhen_AddsCustomCondition()
    {
        var agent = CreateMockAgent("agent1", "Agent 1");

        var workflow = DeliberationFactory.Create()
            .AddAgent(agent.Object)
            .TerminateWhen(
                state => state.Transcript.Count >= 10,
                "Too many messages")
            .Build();

        workflow.Should().NotBeNull();
    }

    [Fact]
    public void DeliberationBuilder_WithConsensusVoting_SetsVotingStrategy()
    {
        var agent = CreateMockAgent("agent1", "Agent 1");

        var workflow = DeliberationFactory.Create()
            .AddAgent(agent.Object)
            .WithMaxRounds(3)
            .WithConsensusVoting()
            .Build();

        workflow.Should().NotBeNull();
    }

    [Fact]
    public void DeliberationBuilder_WithAggregation_SetsVotingStrategy()
    {
        var agent = CreateMockAgent("agent1", "Agent 1");

        var workflow = DeliberationFactory.Create()
            .AddAgent(agent.Object)
            .WithMaxRounds(3)
            .WithAggregation()
            .Build();

        workflow.Should().NotBeNull();
    }

    [Fact]
    public void DeliberationBuilder_WithMajorityVoting_SetsVotingStrategy()
    {
        var agent = CreateMockAgent("agent1", "Agent 1");

        var workflow = DeliberationFactory.Create()
            .AddAgent(agent.Object)
            .WithMaxRounds(3)
            .WithMajorityVoting()
            .Build();

        workflow.Should().NotBeNull();
    }

    [Fact]
    public void Deliberation_Create_ReturnsStringBuilder()
    {
        var builder = DeliberationFactory.Create();

        builder.Should().BeOfType<DeliberationBuilder<string>>();
    }

    [Fact]
    public void Deliberation_CreateGeneric_ReturnsTypedBuilder()
    {
        var builder = DeliberationFactory.Create<TestOutput>();

        builder.Should().BeOfType<DeliberationBuilder<TestOutput>>();
    }

    [Fact]
    public void DeliberationBuilder_ComplexConfiguration_Works()
    {
        var agents = new[]
        {
            CreateMockAgent("analyst", "Analyst").Object,
            CreateMockAgent("creative", "Creative").Object,
            CreateMockAgent("critic", "Critic").Object
        };

        var workflow = DeliberationFactory.Create()
            .WithName("Strategic Planning")
            .AddAgents(agents)
            .WithDebate()
            .WithBudget(b => b
                .WithMaxRounds(5)
                .WithMaxTokens(50000)
                .WithMaxTime(TimeSpan.FromMinutes(10))
                .WithConvergenceThreshold(0.85))
            .WithConsensusVoting()
            .Build();

        workflow.Name.Should().Be("Strategic Planning");
        workflow.Agents.Should().HaveCount(3);
        workflow.Mode.Should().Be(DeliberationMode.Debate);
    }

    private class TestOutput
    {
        public string Summary { get; set; } = string.Empty;
        public List<string> Points { get; set; } = new();
    }
}
