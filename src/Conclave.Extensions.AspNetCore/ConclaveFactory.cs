using Conclave.Abstractions;
using Conclave.Models;
using Conclave.Voting;
using Conclave.Workflows;

namespace Conclave.Extensions.AspNetCore;

public class ConclaveFactory : IConclaveFactory
{
    private readonly List<IAgent> _agents;

    public ProviderRegistry Providers { get; }
    public IReadOnlyList<IAgent> Agents => _agents.AsReadOnly();

    public ConclaveFactory(
        ProviderRegistry providers,
        IEnumerable<IAgent> agents)
    {
        Providers = providers;
        _agents = agents.ToList();
    }

    public ILlmProvider GetProvider(string key)
    {
        return Providers.Get(key)
            ?? throw new InvalidOperationException($"Provider '{key}' not found");
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
            throw new InvalidOperationException("No agents have been configured");
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
            throw new InvalidOperationException("No agents have been configured");
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
}
