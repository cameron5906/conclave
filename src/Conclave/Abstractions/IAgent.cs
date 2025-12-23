using Conclave.Models;
using Conclave.Tools;

namespace Conclave.Abstractions;

public interface IAgent
{
    string Id { get; }
    string Name { get; }
    AgentPersonality Personality { get; }
    ILlmProvider Provider { get; }
    IReadOnlyList<ToolDefinition> AvailableTools { get; }

    Task<AgentResponse> ProcessAsync(
        string task,
        IReadOnlyList<Message>? context = null,
        CancellationToken cancellationToken = default);

    Task<AgentResponse> ProcessWithStructuredOutputAsync<T>(
        string task,
        IReadOnlyList<Message>? context = null,
        CancellationToken cancellationToken = default) where T : class;

    Task<AgentResponse> VoteAsync(
        string task,
        IReadOnlyList<AgentResponse> otherResponses,
        CancellationToken cancellationToken = default);
}

public class AgentPersonality
{
    public string Name { get; init; } = "Assistant";
    public string Description { get; init; } = string.Empty;
    public string SystemPrompt { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Traits { get; init; } = new Dictionary<string, string>();
    public double Creativity { get; init; } = 0.7;
    public double Precision { get; init; } = 0.8;
    public string? Expertise { get; init; }
    public CommunicationStyle CommunicationStyle { get; init; } = CommunicationStyle.Professional;

    public static AgentPersonality Default => new();

    public static AgentPersonality Analyst => new()
    {
        Name = "Analyst",
        Description = "Methodical and data-driven",
        SystemPrompt = "You are an analytical expert. Focus on data, facts, and logical reasoning. Be thorough and systematic in your analysis.",
        Creativity = 0.3,
        Precision = 0.95,
        CommunicationStyle = CommunicationStyle.Technical
    };

    public static AgentPersonality Creative => new()
    {
        Name = "Creative",
        Description = "Innovative and unconventional",
        SystemPrompt = "You are a creative thinker. Explore novel ideas, think outside the box, and propose innovative solutions.",
        Creativity = 0.95,
        Precision = 0.6,
        CommunicationStyle = CommunicationStyle.Casual
    };

    public static AgentPersonality Critic => new()
    {
        Name = "Critic",
        Description = "Thorough reviewer and devil's advocate",
        SystemPrompt = "You are a critical reviewer. Look for flaws, edge cases, and potential problems. Challenge assumptions and provide constructive criticism.",
        Creativity = 0.5,
        Precision = 0.9,
        CommunicationStyle = CommunicationStyle.Direct
    };

    public static AgentPersonality Diplomat => new()
    {
        Name = "Diplomat",
        Description = "Consensus builder and mediator",
        SystemPrompt = "You are a diplomatic mediator. Focus on finding common ground, synthesizing different viewpoints, and building consensus.",
        Creativity = 0.6,
        Precision = 0.7,
        CommunicationStyle = CommunicationStyle.Empathetic
    };

    public static AgentPersonality Expert(string domain) => new()
    {
        Name = $"{domain} Expert",
        Description = $"Domain expert in {domain}",
        SystemPrompt = $"You are an expert in {domain}. Apply deep domain knowledge to provide authoritative and specialized insights.",
        Expertise = domain,
        Creativity = 0.5,
        Precision = 0.9,
        CommunicationStyle = CommunicationStyle.Technical
    };
}

public enum CommunicationStyle
{
    Professional,
    Technical,
    Casual,
    Direct,
    Empathetic,
    Academic
}
