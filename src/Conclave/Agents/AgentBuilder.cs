using Conclave.Abstractions;
using Conclave.Tools;
using Microsoft.Extensions.Logging;

namespace Conclave.Agents;

public class AgentBuilder
{
    private string _id = Guid.NewGuid().ToString();
    private string _name = "Agent";
    private AgentPersonality _personality = AgentPersonality.Default;
    private ILlmProvider? _provider;
    private readonly List<ToolDefinition> _tools = new();
    private LlmCompletionOptions? _options;
    private ILogger? _logger;

    public AgentBuilder WithId(string id)
    {
        _id = id;
        return this;
    }

    public AgentBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public AgentBuilder WithPersonality(AgentPersonality personality)
    {
        _personality = personality;
        return this;
    }

    public AgentBuilder WithProvider(ILlmProvider provider)
    {
        _provider = provider;
        return this;
    }

    public AgentBuilder WithTool(ToolDefinition tool)
    {
        _tools.Add(tool);
        return this;
    }

    public AgentBuilder WithTools(IEnumerable<ToolDefinition> tools)
    {
        _tools.AddRange(tools);
        return this;
    }

    public AgentBuilder WithOptions(LlmCompletionOptions options)
    {
        _options = options;
        return this;
    }

    public AgentBuilder WithModel(string model)
    {
        _options = (_options ?? new LlmCompletionOptions()) with { Model = model };
        return this;
    }

    public AgentBuilder WithTemperature(double temperature)
    {
        _options = (_options ?? new LlmCompletionOptions()) with { Temperature = temperature };
        return this;
    }

    public AgentBuilder WithMaxTokens(int maxTokens)
    {
        _options = (_options ?? new LlmCompletionOptions()) with { MaxTokens = maxTokens };
        return this;
    }

    public AgentBuilder WithLogger(ILogger logger)
    {
        _logger = logger;
        return this;
    }

    public AgentBuilder AsAnalyst() => WithPersonality(AgentPersonality.Analyst);
    public AgentBuilder AsCreative() => WithPersonality(AgentPersonality.Creative);
    public AgentBuilder AsCritic() => WithPersonality(AgentPersonality.Critic);
    public AgentBuilder AsDiplomat() => WithPersonality(AgentPersonality.Diplomat);
    public AgentBuilder AsExpert(string domain) => WithPersonality(AgentPersonality.Expert(domain));

    public AgentBuilder WithCustomPersonality(Action<PersonalityBuilder> configure)
    {
        var builder = new PersonalityBuilder();
        configure(builder);
        _personality = builder.Build();
        return this;
    }

    public IAgent Build()
    {
        if (_provider == null)
        {
            throw new InvalidOperationException("Provider must be set before building an agent");
        }

        return new ConclaveAgent(
            _id,
            _name,
            _personality,
            _provider,
            _tools,
            _options,
            _logger);
    }
}

public class PersonalityBuilder
{
    private string _name = "Custom";
    private string _description = string.Empty;
    private string _systemPrompt = string.Empty;
    private readonly Dictionary<string, string> _traits = new();
    private double _creativity = 0.7;
    private double _precision = 0.8;
    private string? _expertise;
    private CommunicationStyle _style = CommunicationStyle.Professional;

    public PersonalityBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public PersonalityBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    public PersonalityBuilder WithSystemPrompt(string prompt)
    {
        _systemPrompt = prompt;
        return this;
    }

    public PersonalityBuilder WithTrait(string key, string value)
    {
        _traits[key] = value;
        return this;
    }

    public PersonalityBuilder WithCreativity(double creativity)
    {
        _creativity = Math.Clamp(creativity, 0, 1);
        return this;
    }

    public PersonalityBuilder WithPrecision(double precision)
    {
        _precision = Math.Clamp(precision, 0, 1);
        return this;
    }

    public PersonalityBuilder WithExpertise(string expertise)
    {
        _expertise = expertise;
        return this;
    }

    public PersonalityBuilder WithStyle(CommunicationStyle style)
    {
        _style = style;
        return this;
    }

    public AgentPersonality Build()
    {
        return new AgentPersonality
        {
            Name = _name,
            Description = _description,
            SystemPrompt = _systemPrompt,
            Traits = _traits,
            Creativity = _creativity,
            Precision = _precision,
            Expertise = _expertise,
            CommunicationStyle = _style
        };
    }
}
