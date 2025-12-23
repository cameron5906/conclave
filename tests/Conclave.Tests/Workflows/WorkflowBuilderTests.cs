using FluentAssertions;
using Moq;
using Conclave.Abstractions;
using Conclave.Models;
using Conclave.Voting;
using Conclave.Workflows;

namespace Conclave.Tests.Workflows;

public class WorkflowBuilderTests
{
    private readonly Mock<ILlmProvider> _mockProvider;
    private readonly Mock<IAgent> _mockAgent;

    public WorkflowBuilderTests()
    {
        _mockProvider = new Mock<ILlmProvider>();
        _mockAgent = new Mock<IAgent>();
        _mockAgent.Setup(a => a.Id).Returns("test-agent");
        _mockAgent.Setup(a => a.Name).Returns("Test Agent");
    }

    [Fact]
    public void Build_CreatesWorkflowWithDefaults()
    {
        var workflow = Workflow.Create()
            .AddAgent(_mockAgent.Object)
            .Build();

        workflow.Should().NotBeNull();
        workflow.Agents.Should().HaveCount(1);
    }

    [Fact]
    public void WithName_SetsWorkflowName()
    {
        var workflow = Workflow.Create()
            .WithName("Custom Workflow")
            .AddAgent(_mockAgent.Object)
            .Build();

        workflow.Name.Should().Be("Custom Workflow");
    }

    [Fact]
    public void AddAgents_AddsMultipleAgents()
    {
        var mockAgent2 = new Mock<IAgent>();
        mockAgent2.Setup(a => a.Id).Returns("agent-2");

        var workflow = Workflow.Create()
            .AddAgents(new[] { _mockAgent.Object, mockAgent2.Object })
            .Build();

        workflow.Agents.Should().HaveCount(2);
    }

    [Fact]
    public void WithMajorityVoting_SetsMajorityStrategy()
    {
        var workflow = Workflow.Create()
            .AddAgent(_mockAgent.Object)
            .WithMajorityVoting()
            .Build();

        workflow.VotingStrategy.StrategyType.Should().Be(VotingStrategy.Majority);
    }

    [Fact]
    public void WithWeightedVoting_SetsWeightedStrategy()
    {
        var workflow = Workflow.Create()
            .AddAgent(_mockAgent.Object)
            .WithWeightedVoting()
            .Build();

        workflow.VotingStrategy.StrategyType.Should().Be(VotingStrategy.Weighted);
    }

    [Fact]
    public void WithConsensusVoting_SetsConsensusStrategy()
    {
        var workflow = Workflow.Create()
            .AddAgent(_mockAgent.Object)
            .WithConsensusVoting()
            .Build();

        workflow.VotingStrategy.StrategyType.Should().Be(VotingStrategy.Consensus);
    }

    [Fact]
    public void WithRankedChoiceVoting_SetsRankedChoiceStrategy()
    {
        var workflow = Workflow.Create()
            .AddAgent(_mockAgent.Object)
            .WithRankedChoiceVoting()
            .Build();

        workflow.VotingStrategy.StrategyType.Should().Be(VotingStrategy.RankedChoice);
    }

    [Fact]
    public void WithAggregation_SetsAggregationStrategy()
    {
        var workflow = Workflow.Create()
            .AddAgent(_mockAgent.Object)
            .WithAggregation()
            .Build();

        workflow.VotingStrategy.StrategyType.Should().Be(VotingStrategy.Aggregation);
    }

    [Fact]
    public void WithExpertPanel_SetsExpertPanelStrategy()
    {
        var workflow = Workflow.Create()
            .AddAgent(_mockAgent.Object)
            .WithExpertPanel()
            .Build();

        workflow.VotingStrategy.StrategyType.Should().Be(VotingStrategy.ExpertPanel);
    }

    [Fact]
    public void WithCustomVotingStrategy_SetsCustomStrategy()
    {
        var customStrategy = new Mock<IVotingStrategy>();
        customStrategy.Setup(s => s.StrategyType).Returns(VotingStrategy.Unanimous);

        var workflow = Workflow.Create()
            .AddAgent(_mockAgent.Object)
            .WithVotingStrategy(customStrategy.Object)
            .Build();

        workflow.VotingStrategy.Should().Be(customStrategy.Object);
    }

    [Fact]
    public void Build_ThrowsWithoutAgents()
    {
        var builder = Workflow.Create().WithName("Empty");

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*agent*");
    }

    [Fact]
    public void CreateTyped_CreatesTypedWorkflowBuilder()
    {
        var workflow = Workflow.Create<TestOutput>()
            .AddAgent(_mockAgent.Object)
            .Build();

        workflow.Should().NotBeNull();
    }

    private class TestOutput
    {
        public string Result { get; set; } = string.Empty;
    }
}
