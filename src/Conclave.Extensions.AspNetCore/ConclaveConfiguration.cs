namespace Conclave.Extensions.AspNetCore;

public class ConclaveConfiguration
{
    public const string SectionName = "Conclave";

    public OpenAiConfiguration? OpenAi { get; set; }
    public AnthropicConfiguration? Anthropic { get; set; }
    public GeminiConfiguration? Gemini { get; set; }
    public DefaultsConfiguration Defaults { get; set; } = new();
    public List<AgentConfiguration> Agents { get; set; } = new();
}

public class OpenAiConfiguration
{
    public string ApiKey { get; set; } = string.Empty;
    public string? Organization { get; set; }
    public string? BaseUrl { get; set; }
    public string DefaultModel { get; set; } = "gpt-4o";
    public double? DefaultTemperature { get; set; }
    public int? DefaultMaxTokens { get; set; }
}

public class AnthropicConfiguration
{
    public string ApiKey { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string? ApiVersion { get; set; } = "2023-06-01";
    public string DefaultModel { get; set; } = "claude-sonnet-4-20250514";
    public double? DefaultTemperature { get; set; }
    public int? DefaultMaxTokens { get; set; } = 4096;
}

public class GeminiConfiguration
{
    public string ApiKey { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string DefaultModel { get; set; } = "gemini-2.0-flash";
    public double? DefaultTemperature { get; set; }
    public int? DefaultMaxTokens { get; set; }
}

public class DefaultsConfiguration
{
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public double? Temperature { get; set; }
    public int? MaxTokens { get; set; }
}

public class AgentConfiguration
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? Model { get; set; }
    public PersonalityConfiguration? Personality { get; set; }
}

public class PersonalityConfiguration
{
    public string? Preset { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? SystemPrompt { get; set; }
    public string? Expertise { get; set; }
    public double? Creativity { get; set; }
    public double? Precision { get; set; }
    public string? CommunicationStyle { get; set; }
    public Dictionary<string, string>? Traits { get; set; }
}
