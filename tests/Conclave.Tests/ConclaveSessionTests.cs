using FluentAssertions;
using Moq;
using Conclave.Abstractions;
using Conclave.Models;

namespace Conclave.Tests;

public class ConclaveSessionTests : IDisposable
{
    private readonly ConclaveSession _session;
    private readonly Mock<ILlmProvider> _mockProvider;
    private readonly Mock<IAgent> _mockAgent;

    public ConclaveSessionTests()
    {
        _session = new ConclaveSession();
        _mockProvider = new Mock<ILlmProvider>();
        _mockProvider.Setup(p => p.ProviderId).Returns("mock");

        _mockAgent = new Mock<IAgent>();
        _mockAgent.Setup(a => a.Id).Returns("test-agent");
        _mockAgent.Setup(a => a.Name).Returns("Test Agent");
    }

    [Fact]
    public void AddProvider_AddsProviderToList()
    {
        _session.AddProvider(_mockProvider.Object);

        _session.Providers.Should().HaveCount(1);
        _session.Providers[0].ProviderId.Should().Be("mock");
    }

    [Fact]
    public void AddAgent_AddsAgentToList()
    {
        _session.AddAgent(_mockAgent.Object);

        _session.Agents.Should().HaveCount(1);
        _session.Agents[0].Name.Should().Be("Test Agent");
    }

    [Fact]
    public void AddAgent_WithNameAndProvider_CreatesAndAddsAgent()
    {
        _session
            .AddProvider(_mockProvider.Object)
            .AddAgent("My Agent", _mockProvider.Object);

        _session.Agents.Should().HaveCount(1);
        _session.Agents[0].Name.Should().Be("My Agent");
    }

    [Fact]
    public void AddAgent_ByProviderIndex_UsesCorrectProvider()
    {
        _session
            .AddProvider(_mockProvider.Object)
            .AddAgent("Agent 1", 0);

        _session.Agents.Should().HaveCount(1);
        _session.Agents[0].Provider.Should().Be(_mockProvider.Object);
    }

    [Fact]
    public void AddAgent_WithInvalidIndex_ThrowsArgumentOutOfRange()
    {
        _session.AddProvider(_mockProvider.Object);

        var act = () => _session.AddAgent("Agent", 5);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ExecuteAsync_WithNoAgents_ThrowsInvalidOperation()
    {
        var act = async () => await _session.ExecuteAsync("Test task");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*agent*");
    }

    [Fact]
    public async Task ExecuteAsync_ExecutesWorkflowWithAgents()
    {
        _mockAgent.Setup(a => a.ProcessAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Message>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResponse
            {
                AgentId = "test-agent",
                Response = "Test response"
            });

        _session.AddAgent(_mockAgent.Object);

        var result = await _session.ExecuteAsync("Test task");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Test response");
    }

    [Fact]
    public async Task ExecuteAsync_WithDifferentStrategies_UsesCorrectStrategy()
    {
        _mockAgent.Setup(a => a.ProcessAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Message>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResponse { AgentId = "test", Response = "Response" });

        _session.AddAgent(_mockAgent.Object);

        var result = await _session.ExecuteAsync("Test", VotingStrategy.Aggregation);

        result.VotingResult!.StrategyUsed.Should().Be(VotingStrategy.Aggregation);
    }

    [Fact]
    public void CreateAgent_ReturnsAgentBuilder()
    {
        var builder = _session.CreateAgent();

        builder.Should().NotBeNull();
    }

    [Fact]
    public void CreateWorkflow_ReturnsWorkflowBuilder()
    {
        var builder = _session.CreateWorkflow();

        builder.Should().NotBeNull();
    }

    [Fact]
    public void CreateWorkflow_Generic_ReturnsTypedWorkflowBuilder()
    {
        var builder = _session.CreateWorkflow<TestOutput>();

        builder.Should().NotBeNull();
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var act = () =>
        {
            _session.Dispose();
            _session.Dispose();
        };

        act.Should().NotThrow();
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    private class TestOutput
    {
        public string Value { get; set; } = string.Empty;
    }
}
