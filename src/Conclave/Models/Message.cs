namespace Conclave.Models;

public enum MessageRole
{
    System,
    User,
    Assistant,
    Tool
}

public record Message
{
    public MessageRole Role { get; init; }
    public string Content { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string? ToolCallId { get; init; }
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }

    public static Message System(string content) => new() { Role = MessageRole.System, Content = content };
    public static Message User(string content) => new() { Role = MessageRole.User, Content = content };
    public static Message Assistant(string content) => new() { Role = MessageRole.Assistant, Content = content };
    public static Message Tool(string content, string toolCallId) => new() { Role = MessageRole.Tool, Content = content, ToolCallId = toolCallId };
}

public class ToolCall
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Arguments { get; init; } = string.Empty;
}
