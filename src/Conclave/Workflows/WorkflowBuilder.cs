using Conclave.Abstractions;
using Conclave.Agents;
using Conclave.Deliberation;
using Conclave.Voting;
using Microsoft.Extensions.Logging;

namespace Conclave.Workflows;

public class WorkflowBuilder<TOutput> where TOutput : class
{
    private string _name = "Conclave Workflow";
    private readonly List<IAgent> _agents = new();
    private IVotingStrategy _votingStrategy = new MajorityVotingStrategy();
    private VotingContext _votingContext = new();
    private ILogger? _logger;

    public WorkflowBuilder<TOutput> WithName(string name)
    {
        _name = name;
        return this;
    }

    public WorkflowBuilder<TOutput> AddAgent(IAgent agent)
    {
        _agents.Add(agent);
        return this;
    }

    public WorkflowBuilder<TOutput> AddAgents(IEnumerable<IAgent> agents)
    {
        _agents.AddRange(agents);
        return this;
    }

    public WorkflowBuilder<TOutput> AddAgent(Action<AgentBuilder> configure)
    {
        var builder = new AgentBuilder();
        configure(builder);
        _agents.Add(builder.Build());
        return this;
    }

    public WorkflowBuilder<TOutput> WithVotingStrategy(IVotingStrategy strategy)
    {
        _votingStrategy = strategy;
        return this;
    }

    public WorkflowBuilder<TOutput> WithMajorityVoting()
    {
        _votingStrategy = new MajorityVotingStrategy();
        return this;
    }

    public WorkflowBuilder<TOutput> WithWeightedVoting()
    {
        _votingStrategy = new WeightedVotingStrategy();
        return this;
    }

    public WorkflowBuilder<TOutput> WithConsensusVoting()
    {
        _votingStrategy = new ConsensusVotingStrategy();
        return this;
    }

    public WorkflowBuilder<TOutput> WithRankedChoiceVoting()
    {
        _votingStrategy = new RankedChoiceVotingStrategy();
        return this;
    }

    public WorkflowBuilder<TOutput> WithAggregation()
    {
        _votingStrategy = new AggregationVotingStrategy();
        return this;
    }

    public WorkflowBuilder<TOutput> WithExpertPanel()
    {
        _votingStrategy = new ExpertPanelVotingStrategy();
        return this;
    }

    public WorkflowBuilder<TOutput> WithVotingContext(VotingContext context)
    {
        _votingContext = context;
        return this;
    }

    public WorkflowBuilder<TOutput> WithAgentWeight(string agentId, double weight)
    {
        var weights = new Dictionary<string, double>(_votingContext.AgentWeights)
        {
            [agentId] = weight
        };
        _votingContext = _votingContext with { AgentWeights = weights };
        return this;
    }

    public WorkflowBuilder<TOutput> WithArbiter(ILlmProvider arbiter)
    {
        _votingContext = _votingContext with { ArbiterProvider = arbiter };
        return this;
    }

    public WorkflowBuilder<TOutput> WithConsensusThreshold(double threshold)
    {
        _votingContext = _votingContext with { RequiredConsensusThreshold = threshold };
        return this;
    }

    public WorkflowBuilder<TOutput> WithLogger(ILogger logger)
    {
        _logger = logger;
        return this;
    }

    public IWorkflow<TOutput> Build()
    {
        if (!_agents.Any())
        {
            throw new InvalidOperationException("At least one agent must be added to the workflow");
        }

        return new ConclaveWorkflow<TOutput>(
            _name,
            _agents,
            _votingStrategy,
            _votingContext,
            _logger);
    }

    public DeliberationBuilder<TOutput> WithDeliberation()
    {
        var builder = new DeliberationBuilder<TOutput>()
            .WithName(_name)
            .AddAgents(_agents)
            .WithVotingStrategy(_votingStrategy)
            .WithVotingContext(_votingContext);

        if (_logger != null)
            builder.WithLogger(_logger);

        return builder;
    }

    public DeliberationBuilder<TOutput> WithDeliberation(Action<DeliberationBudget> configureBudget)
    {
        var budget = new DeliberationBudget();
        configureBudget(budget);

        return WithDeliberation().WithBudget(budget);
    }
}

public static class Workflow
{
    public static WorkflowBuilder<string> Create() => new WorkflowBuilder<string>();

    public static WorkflowBuilder<TOutput> Create<TOutput>() where TOutput : class
        => new WorkflowBuilder<TOutput>();

    public static DeliberationBuilder<string> CreateDeliberation() => new();

    public static DeliberationBuilder<TOutput> CreateDeliberation<TOutput>() where TOutput : class
        => new();
}
