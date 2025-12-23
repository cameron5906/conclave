using FluentAssertions;
using Moq;
using Conclave.Abstractions;
using Conclave.Agents;
using Conclave.Models;
using Conclave.Tools;

namespace Conclave.Tests.Agents;

public class ConclaveAgentTests
{
    private readonly Mock<ILlmProvider> _mockProvider;

    public ConclaveAgentTests()
    {
        _mockProvider = new Mock<ILlmProvider>();
        _mockProvider.Setup(p => p.ProviderId).Returns("mock");
        _mockProvider.Setup(p => p.DisplayName).Returns("Mock Provider");
    }

    [Fact]
    public async Task ProcessAsync_ReturnsAgentResponse()
    {
        _mockProvider.Setup(p => p.CompleteAsync(
            It.IsAny<IReadOnlyList<Message>>(),
            It.IsAny<LlmCompletionOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse
            {
                Content = "This is the response",
                Usage = new CompletionUsage { PromptTokens = 10, CompletionTokens = 20 }
            });

        var agent = new AgentBuilder()
            .WithId("test-agent")
            .WithName("Test Agent")
            .WithProvider(_mockProvider.Object)
            .Build();

        var response = await agent.ProcessAsync("What is 2+2?");

        response.AgentId.Should().Be("test-agent");
        response.AgentName.Should().Be("Test Agent");
        response.Response.Should().Be("This is the response");
        response.Usage.Should().NotBeNull();
        response.Usage!.TotalTokens.Should().Be(30);
    }

    [Fact]
    public async Task ProcessAsync_WithContext_IncludesContextInMessages()
    {
        var capturedMessages = new List<Message>();

        _mockProvider.Setup(p => p.CompleteAsync(
            It.IsAny<IReadOnlyList<Message>>(),
            It.IsAny<LlmCompletionOptions>(),
            It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<Message>, LlmCompletionOptions?, CancellationToken>((msgs, opts, ct) =>
            {
                capturedMessages.AddRange(msgs);
            })
            .ReturnsAsync(new LlmResponse { Content = "Response" });

        var agent = new AgentBuilder()
            .WithProvider(_mockProvider.Object)
            .Build();

        var context = new List<Message>
        {
            Message.User("Previous question"),
            Message.Assistant("Previous answer")
        };

        await agent.ProcessAsync("Follow-up question", context);

        capturedMessages.Should().HaveCount(3);
        capturedMessages[0].Content.Should().Be("Previous question");
        capturedMessages[1].Content.Should().Be("Previous answer");
        capturedMessages[2].Content.Should().Be("Follow-up question");
    }

    [Fact]
    public async Task ProcessAsync_WithTools_ExecutesToolsAndReturnsResult()
    {
        var toolExecuted = false;
        var tool = new ToolBuilder()
            .WithName("calculator")
            .WithDescription("Calculate a result")
            .WithHandler(async (args) =>
            {
                toolExecuted = true;
                return ToolResult.Ok("42");
            })
            .Build();

        _mockProvider.SetupSequence(p => p.CompleteWithToolsAsync(
            It.IsAny<IReadOnlyList<Message>>(),
            It.IsAny<IReadOnlyList<ToolDefinition>>(),
            It.IsAny<LlmCompletionOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse
            {
                Content = "",
                ToolCalls = new List<ToolCall>
                {
                    new() { Id = "call_1", Name = "calculator", Arguments = "{}" }
                }
            })
            .ReturnsAsync(new LlmResponse
            {
                Content = "The answer is 42"
            });

        var agent = new AgentBuilder()
            .WithProvider(_mockProvider.Object)
            .WithTool(tool)
            .Build();

        var response = await agent.ProcessAsync("Calculate something");

        toolExecuted.Should().BeTrue();
        response.Response.Should().Be("The answer is 42");
    }

    [Fact]
    public async Task VoteAsync_ReturnsVoteWithReasoning()
    {
        _mockProvider.Setup(p => p.CompleteAsync(
            It.IsAny<IReadOnlyList<Message>>(),
            It.IsAny<LlmCompletionOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse
            {
                Content = "I vote for response 2 because it is more comprehensive."
            });

        var agent = new AgentBuilder()
            .WithProvider(_mockProvider.Object)
            .Build();

        var otherResponses = new List<AgentResponse>
        {
            new() { AgentId = "agent1", AgentName = "Agent 1", Response = "Answer A" },
            new() { AgentId = "agent2", AgentName = "Agent 2", Response = "Answer B" }
        };

        var vote = await agent.VoteAsync("Which is better?", otherResponses);

        vote.Response.Should().Contain("response 2");
        vote.StructuredOutput.Should().BeOfType<VoteResult>();
        var voteResult = (VoteResult)vote.StructuredOutput!;
        voteResult.ChosenAgentId.Should().Be("agent2");
    }

    [Fact]
    public async Task ProcessAsync_HandlesProviderError()
    {
        _mockProvider.Setup(p => p.CompleteAsync(
            It.IsAny<IReadOnlyList<Message>>(),
            It.IsAny<LlmCompletionOptions>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Provider error"));

        var agent = new AgentBuilder()
            .WithProvider(_mockProvider.Object)
            .Build();

        var response = await agent.ProcessAsync("Test");

        response.Response.Should().Contain("Error");
        response.Response.Should().Contain("Provider error");
    }
}
