using FluentAssertions;
using Moq;
using Conclave.Abstractions;
using Conclave.Models;
using Conclave.Voting;
using Conclave.Workflows;

namespace Conclave.Tests.Workflows;

public class ConclaveWorkflowTests
{
    private readonly Mock<IAgent> _mockAgent1;
    private readonly Mock<IAgent> _mockAgent2;
    private readonly Mock<IVotingStrategy> _mockVotingStrategy;

    public ConclaveWorkflowTests()
    {
        _mockAgent1 = new Mock<IAgent>();
        _mockAgent1.Setup(a => a.Id).Returns("agent1");
        _mockAgent1.Setup(a => a.Name).Returns("Agent 1");

        _mockAgent2 = new Mock<IAgent>();
        _mockAgent2.Setup(a => a.Id).Returns("agent2");
        _mockAgent2.Setup(a => a.Name).Returns("Agent 2");

        _mockVotingStrategy = new Mock<IVotingStrategy>();
        _mockVotingStrategy.Setup(s => s.StrategyType).Returns(VotingStrategy.Majority);
    }

    [Fact]
    public async Task ExecuteAsync_CollectsResponsesFromAllAgents()
    {
        _mockAgent1.Setup(a => a.ProcessAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Message>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResponse { AgentId = "agent1", Response = "Response 1" });

        _mockAgent2.Setup(a => a.ProcessAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Message>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResponse { AgentId = "agent2", Response = "Response 2" });

        _mockVotingStrategy.Setup(s => s.EvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<AgentResponse>>(),
            It.IsAny<VotingContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VotingResult
            {
                WinningResponse = "Response 1",
                WinningAgentId = "agent1",
                ConsensusScore = 0.5
            });

        var workflow = new ConclaveWorkflow<string>(
            "Test Workflow",
            new[] { _mockAgent1.Object, _mockAgent2.Object },
            _mockVotingStrategy.Object);

        var result = await workflow.ExecuteAsync("Test task");

        result.IsSuccess.Should().BeTrue();
        result.AgentResponses.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsVotingResult()
    {
        _mockAgent1.Setup(a => a.ProcessAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Message>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResponse { AgentId = "agent1", Response = "Winner" });

        _mockVotingStrategy.Setup(s => s.EvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<AgentResponse>>(),
            It.IsAny<VotingContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VotingResult
            {
                WinningResponse = "Winner",
                WinningAgentId = "agent1",
                StrategyUsed = VotingStrategy.Majority,
                ConsensusScore = 1.0
            });

        var workflow = new ConclaveWorkflow<string>(
            "Test",
            new[] { _mockAgent1.Object },
            _mockVotingStrategy.Object);

        var result = await workflow.ExecuteAsync("Test");

        result.VotingResult.Should().NotBeNull();
        result.VotingResult!.WinningResponse.Should().Be("Winner");
        result.VotingResult.ConsensusScore.Should().Be(1.0);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsProgress()
    {
        var progressReports = new List<WorkflowProgress>();

        _mockAgent1.Setup(a => a.ProcessAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Message>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResponse { AgentId = "agent1", Response = "Response" });

        _mockVotingStrategy.Setup(s => s.EvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<AgentResponse>>(),
            It.IsAny<VotingContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VotingResult { WinningResponse = "Response", ConsensusScore = 1.0 });

        var workflow = new ConclaveWorkflow<string>(
            "Test",
            new[] { _mockAgent1.Object },
            _mockVotingStrategy.Object);

        var options = new WorkflowOptions
        {
            OnProgress = p => progressReports.Add(p)
        };

        await workflow.ExecuteAsync("Test", options);

        progressReports.Should().NotBeEmpty();
        progressReports.Should().Contain(p => p.Stage == WorkflowStage.Initializing);
        progressReports.Should().Contain(p => p.Stage == WorkflowStage.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_WithParallelExecution_ProcessesInParallel()
    {
        var executionOrder = new List<string>();
        var semaphore = new SemaphoreSlim(0);

        _mockAgent1.Setup(a => a.ProcessAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Message>?>(),
            It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                executionOrder.Add("agent1-start");
                await semaphore.WaitAsync();
                executionOrder.Add("agent1-end");
                return new AgentResponse { AgentId = "agent1", Response = "1" };
            });

        _mockAgent2.Setup(a => a.ProcessAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Message>?>(),
            It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                executionOrder.Add("agent2-start");
                semaphore.Release(2);
                executionOrder.Add("agent2-end");
                return new AgentResponse { AgentId = "agent2", Response = "2" };
            });

        _mockVotingStrategy.Setup(s => s.EvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<AgentResponse>>(),
            It.IsAny<VotingContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VotingResult { WinningResponse = "1", ConsensusScore = 0.5 });

        var workflow = new ConclaveWorkflow<string>(
            "Test",
            new[] { _mockAgent1.Object, _mockAgent2.Object },
            _mockVotingStrategy.Object);

        var options = new WorkflowOptions { EnableParallelExecution = true };

        await workflow.ExecuteAsync("Test", options);

        executionOrder.Should().Contain("agent1-start");
        executionOrder.Should().Contain("agent2-start");
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_ThrowsOperationCanceled()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockAgent1.Setup(a => a.ProcessAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Message>?>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var workflow = new ConclaveWorkflow<string>(
            "Test",
            new[] { _mockAgent1.Object },
            _mockVotingStrategy.Object);

        var result = await workflow.ExecuteAsync("Test", cancellationToken: cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("cancelled");
    }
}
