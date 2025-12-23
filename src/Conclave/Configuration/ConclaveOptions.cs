using Conclave.Providers;

namespace Conclave.Configuration;

public class ConclaveOptions
{
    public OpenAiOptions? OpenAi { get; set; }
    public AnthropicOptions? Anthropic { get; set; }
    public GeminiOptions? Gemini { get; set; }
    public DefaultProviderOptions Defaults { get; set; } = new();
}

public class DefaultProviderOptions
{
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public double? Temperature { get; set; }
    public int? MaxTokens { get; set; }
}
