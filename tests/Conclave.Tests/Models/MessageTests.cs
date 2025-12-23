using FluentAssertions;
using Conclave.Models;

namespace Conclave.Tests.Models;

public class MessageTests
{
    [Fact]
    public void System_CreatesSystemMessage()
    {
        var message = Message.System("You are a helpful assistant");

        message.Role.Should().Be(MessageRole.System);
        message.Content.Should().Be("You are a helpful assistant");
    }

    [Fact]
    public void User_CreatesUserMessage()
    {
        var message = Message.User("Hello, world!");

        message.Role.Should().Be(MessageRole.User);
        message.Content.Should().Be("Hello, world!");
    }

    [Fact]
    public void Assistant_CreatesAssistantMessage()
    {
        var message = Message.Assistant("Hello! How can I help?");

        message.Role.Should().Be(MessageRole.Assistant);
        message.Content.Should().Be("Hello! How can I help?");
    }

    [Fact]
    public void Tool_CreatesToolMessage()
    {
        var message = Message.Tool("Result: 42", "call_123");

        message.Role.Should().Be(MessageRole.Tool);
        message.Content.Should().Be("Result: 42");
        message.ToolCallId.Should().Be("call_123");
    }
}
