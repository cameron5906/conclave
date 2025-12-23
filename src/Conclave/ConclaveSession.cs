using Conclave.Abstractions;
using Conclave.Agents;
using Conclave.Models;
using Conclave.Providers;
using Conclave.Voting;
using Conclave.Workflows;

namespace Conclave;

public class ConclaveSession : IDisposable
{
    private readonly List<ILlmProvider> _providers = new();
    private readonly List<IAgent> _agents = new();
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public IReadOnlyList<ILlmProvider> Providers => _providers.AsReadOnly();
    public IReadOnlyList<IAgent> Agents => _agents.AsReadOnly();

    public ConclaveSession()
    {
        _httpClient = new HttpClient();
    }

    public ConclaveSession AddOpenAi(string apiKey, string? model = null)
    {
        var provider = new OpenAiProvider(new HttpClient(), new OpenAiOptions
        {
            ApiKey = apiKey,
            DefaultModel = model ?? "gpt-4o"
        });
        _providers.Add(provider);
        return this;
    }

    public ConclaveSession AddAnthropic(string apiKey, string? model = null)
    {
        var provider = new AnthropicProvider(new HttpClient(), new AnthropicOptions
        {
            ApiKey = apiKey,
            DefaultModel = model ?? "claude-sonnet-4-20250514"
        });
        _providers.Add(provider);
        return this;
    }

    public ConclaveSession AddGemini(string apiKey, string? model = null)
    {
        var provider = new GeminiProvider(new HttpClient(), new GeminiOptions
        {
            ApiKey = apiKey,
            DefaultModel = model ?? "gemini-2.0-flash"
        });
        _providers.Add(provider);
        return this;
    }

    public ConclaveSession AddProvider(ILlmProvider provider)
    {
        _providers.Add(provider);
        return this;
    }

    public ConclaveSession AddAgent(IAgent agent)
    {
        _agents.Add(agent);
        return this;
    }

    public ConclaveSession AddAgent(string name, ILlmProvider provider, AgentPersonality? personality = null)
    {
        var agent = new AgentBuilder()
            .WithName(name)
            .WithProvider(provider)
            .WithPersonality(personality ?? AgentPersonality.Default)
            .Build();
        _agents.Add(agent);
        return this;
    }

    public ConclaveSession AddAgent(string name, int providerIndex, AgentPersonality? personality = null)
    {
        if (providerIndex < 0 || providerIndex >= _providers.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(providerIndex));
        }
        return AddAgent(name, _providers[providerIndex], personality);
    }

    public AgentBuilder CreateAgent()
    {
        return new AgentBuilder();
    }

    public WorkflowBuilder<string> CreateWorkflow()
    {
        return Workflow.Create();
    }

    public WorkflowBuilder<TOutput> CreateWorkflow<TOutput>() where TOutput : class
    {
        return Workflow.Create<TOutput>();
    }

    public async Task<TaskResult<string>> ExecuteAsync(
        string task,
        VotingStrategy strategy = VotingStrategy.Majority,
        WorkflowOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!_agents.Any())
        {
            throw new InvalidOperationException("No agents have been added to the session");
        }

        var votingStrategy = CreateVotingStrategy(strategy);

        var workflow = new ConclaveWorkflow<string>(
            "Quick Execution",
            _agents,
            votingStrategy);

        return await workflow.ExecuteAsync(task, options, cancellationToken);
    }

    public async Task<TaskResult<TOutput>> ExecuteAsync<TOutput>(
        string task,
        VotingStrategy strategy = VotingStrategy.Majority,
        WorkflowOptions? options = null,
        CancellationToken cancellationToken = default) where TOutput : class
    {
        if (!_agents.Any())
        {
            throw new InvalidOperationException("No agents have been added to the session");
        }

        var votingStrategy = CreateVotingStrategy(strategy);

        var workflow = new ConclaveWorkflow<TOutput>(
            "Quick Execution",
            _agents,
            votingStrategy);

        return await workflow.ExecuteAsync(task, options, cancellationToken);
    }

    private static IVotingStrategy CreateVotingStrategy(VotingStrategy strategy)
    {
        return strategy switch
        {
            VotingStrategy.Majority => new MajorityVotingStrategy(),
            VotingStrategy.Weighted => new WeightedVotingStrategy(),
            VotingStrategy.Consensus => new ConsensusVotingStrategy(),
            VotingStrategy.RankedChoice => new RankedChoiceVotingStrategy(),
            VotingStrategy.Aggregation => new AggregationVotingStrategy(),
            VotingStrategy.ExpertPanel => new ExpertPanelVotingStrategy(),
            _ => new MajorityVotingStrategy()
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _disposed = true;
        }
    }
}
