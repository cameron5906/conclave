using FluentAssertions;
using Moq;
using Conclave.Abstractions;
using Conclave.Agents;
using Conclave.Tools;

namespace Conclave.Tests.Agents;

public class AgentBuilderTests
{
    private readonly Mock<ILlmProvider> _mockProvider;

    public AgentBuilderTests()
    {
        _mockProvider = new Mock<ILlmProvider>();
        _mockProvider.Setup(p => p.ProviderId).Returns("mock");
        _mockProvider.Setup(p => p.DisplayName).Returns("Mock Provider");
    }

    [Fact]
    public void Build_CreatesAgentWithDefaultValues()
    {
        var agent = new AgentBuilder()
            .WithProvider(_mockProvider.Object)
            .Build();

        agent.Should().NotBeNull();
        agent.Name.Should().Be("Agent");
        agent.Provider.Should().Be(_mockProvider.Object);
        agent.Personality.Should().NotBeNull();
    }

    [Fact]
    public void WithName_SetsAgentName()
    {
        var agent = new AgentBuilder()
            .WithName("Research Agent")
            .WithProvider(_mockProvider.Object)
            .Build();

        agent.Name.Should().Be("Research Agent");
    }

    [Fact]
    public void WithId_SetsAgentId()
    {
        var agent = new AgentBuilder()
            .WithId("agent-001")
            .WithProvider(_mockProvider.Object)
            .Build();

        agent.Id.Should().Be("agent-001");
    }

    [Fact]
    public void AsAnalyst_SetsAnalystPersonality()
    {
        var agent = new AgentBuilder()
            .WithProvider(_mockProvider.Object)
            .AsAnalyst()
            .Build();

        agent.Personality.Name.Should().Be("Analyst");
        agent.Personality.Precision.Should().BeGreaterThan(0.9);
    }

    [Fact]
    public void AsCreative_SetsCreativePersonality()
    {
        var agent = new AgentBuilder()
            .WithProvider(_mockProvider.Object)
            .AsCreative()
            .Build();

        agent.Personality.Name.Should().Be("Creative");
        agent.Personality.Creativity.Should().BeGreaterThan(0.9);
    }

    [Fact]
    public void AsCritic_SetsCriticPersonality()
    {
        var agent = new AgentBuilder()
            .WithProvider(_mockProvider.Object)
            .AsCritic()
            .Build();

        agent.Personality.Name.Should().Be("Critic");
    }

    [Fact]
    public void AsDiplomat_SetsDiplomatPersonality()
    {
        var agent = new AgentBuilder()
            .WithProvider(_mockProvider.Object)
            .AsDiplomat()
            .Build();

        agent.Personality.Name.Should().Be("Diplomat");
    }

    [Fact]
    public void AsExpert_SetsExpertPersonality()
    {
        var agent = new AgentBuilder()
            .WithProvider(_mockProvider.Object)
            .AsExpert("Machine Learning")
            .Build();

        agent.Personality.Name.Should().Be("Machine Learning Expert");
        agent.Personality.Expertise.Should().Be("Machine Learning");
    }

    [Fact]
    public void WithCustomPersonality_SetsCustomProperties()
    {
        var agent = new AgentBuilder()
            .WithProvider(_mockProvider.Object)
            .WithCustomPersonality(p => p
                .WithName("Custom Agent")
                .WithDescription("A custom personality")
                .WithSystemPrompt("You are a specialized agent")
                .WithCreativity(0.5)
                .WithPrecision(0.9)
                .WithStyle(CommunicationStyle.Academic))
            .Build();

        agent.Personality.Name.Should().Be("Custom Agent");
        agent.Personality.Description.Should().Be("A custom personality");
        agent.Personality.Creativity.Should().Be(0.5);
        agent.Personality.Precision.Should().Be(0.9);
        agent.Personality.CommunicationStyle.Should().Be(CommunicationStyle.Academic);
    }

    [Fact]
    public void WithTool_AddsToolToAgent()
    {
        var tool = new ToolBuilder()
            .WithName("search")
            .WithDescription("Search tool")
            .Build();

        var agent = new AgentBuilder()
            .WithProvider(_mockProvider.Object)
            .WithTool(tool)
            .Build();

        agent.AvailableTools.Should().HaveCount(1);
        agent.AvailableTools[0].Name.Should().Be("search");
    }

    [Fact]
    public void WithTools_AddsMultipleTools()
    {
        var tools = new[]
        {
            new ToolBuilder().WithName("tool1").WithDescription("Tool 1").Build(),
            new ToolBuilder().WithName("tool2").WithDescription("Tool 2").Build()
        };

        var agent = new AgentBuilder()
            .WithProvider(_mockProvider.Object)
            .WithTools(tools)
            .Build();

        agent.AvailableTools.Should().HaveCount(2);
    }

    [Fact]
    public void Build_ThrowsWithoutProvider()
    {
        var builder = new AgentBuilder().WithName("Test");

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Provider*");
    }
}
