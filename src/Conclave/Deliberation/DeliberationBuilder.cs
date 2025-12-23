using Microsoft.Extensions.Logging;
using Conclave.Abstractions;
using Conclave.Context;
using Conclave.Voting;

namespace Conclave.Deliberation;

public class DeliberationBuilder<TOutput> where TOutput : class
{
    private string _name = "Deliberation";
    private readonly List<IAgent> _agents = new();
    private DeliberationMode _mode = DeliberationMode.RoundRobin;
    private DeliberationBudget? _budget;
    private IVotingStrategy? _finalVotingStrategy;
    private IConvergenceCalculator? _convergenceCalculator;
    private IAgent? _moderator;
    private VotingContext? _votingContext;
    private ILogger? _logger;
    private ILlmProvider? _arbiter;
    private IContextManager? _contextManager;
    private ContextWindowOptions? _contextOptions;

    public DeliberationBuilder<TOutput> WithName(string name)
    {
        _name = name;
        return this;
    }

    public DeliberationBuilder<TOutput> AddAgent(IAgent agent)
    {
        _agents.Add(agent);
        return this;
    }

    public DeliberationBuilder<TOutput> AddAgents(IEnumerable<IAgent> agents)
    {
        _agents.AddRange(agents);
        return this;
    }

    public DeliberationBuilder<TOutput> WithMode(DeliberationMode mode)
    {
        _mode = mode;
        return this;
    }

    public DeliberationBuilder<TOutput> WithRoundRobin()
    {
        _mode = DeliberationMode.RoundRobin;
        return this;
    }

    public DeliberationBuilder<TOutput> WithDebate()
    {
        _mode = DeliberationMode.Debate;
        return this;
    }

    public DeliberationBuilder<TOutput> WithModeration(IAgent moderator)
    {
        _mode = DeliberationMode.Moderated;
        _moderator = moderator;
        return this;
    }

    public DeliberationBuilder<TOutput> WithFreeForm()
    {
        _mode = DeliberationMode.FreeForm;
        return this;
    }

    public DeliberationBuilder<TOutput> WithBudget(Action<DeliberationBudget> configure)
    {
        _budget = new DeliberationBudget();
        configure(_budget);
        return this;
    }

    public DeliberationBuilder<TOutput> WithBudget(DeliberationBudget budget)
    {
        _budget = budget;
        return this;
    }

    public DeliberationBuilder<TOutput> WithMaxRounds(int rounds)
    {
        _budget ??= new DeliberationBudget();
        _budget.WithMaxRounds(rounds);
        return this;
    }

    public DeliberationBuilder<TOutput> WithMaxTokens(int tokens)
    {
        _budget ??= new DeliberationBudget();
        _budget.WithMaxTokens(tokens);
        return this;
    }

    public DeliberationBuilder<TOutput> WithMaxTime(TimeSpan time)
    {
        _budget ??= new DeliberationBudget();
        _budget.WithMaxTime(time);
        return this;
    }

    public DeliberationBuilder<TOutput> WithConvergenceThreshold(double threshold, int minRounds = 2)
    {
        _budget ??= new DeliberationBudget();
        _budget.WithConvergenceThreshold(threshold, minRounds);
        return this;
    }

    public DeliberationBuilder<TOutput> WithAgentTerminator(
        IAgent agent,
        string? customPrompt = null,
        double confidenceThreshold = 0.7)
    {
        _budget ??= new DeliberationBudget();
        _budget.WithAgentTerminator(agent, customPrompt, confidenceThreshold);
        return this;
    }

    public DeliberationBuilder<TOutput> WithWorkflowTerminator(
        IWorkflow<TerminatorResponse> workflow,
        double confidenceThreshold = 0.7)
    {
        _budget ??= new DeliberationBudget();
        _budget.WithWorkflowTerminator(workflow, confidenceThreshold);
        return this;
    }

    public DeliberationBuilder<TOutput> TerminateWhen(
        Func<DeliberationState, bool> condition,
        string description = "Custom condition")
    {
        _budget ??= new DeliberationBudget();
        _budget.WithCustomCondition(condition, description);
        return this;
    }

    public DeliberationBuilder<TOutput> TerminateWhenAsync(
        Func<DeliberationState, Task<bool>> condition,
        string description = "Custom async condition")
    {
        _budget ??= new DeliberationBudget();
        _budget.WithCustomConditionAsync(condition, description);
        return this;
    }

    public DeliberationBuilder<TOutput> WithConsensusVoting()
    {
        _finalVotingStrategy = new ConsensusVotingStrategy();
        return this;
    }

    public DeliberationBuilder<TOutput> WithAggregation()
    {
        _finalVotingStrategy = new AggregationVotingStrategy();
        return this;
    }

    public DeliberationBuilder<TOutput> WithMajorityVoting()
    {
        _finalVotingStrategy = new MajorityVotingStrategy();
        return this;
    }

    public DeliberationBuilder<TOutput> WithExpertPanel()
    {
        _finalVotingStrategy = new ExpertPanelVotingStrategy();
        return this;
    }

    public DeliberationBuilder<TOutput> WithVotingStrategy(IVotingStrategy strategy)
    {
        _finalVotingStrategy = strategy;
        return this;
    }

    public DeliberationBuilder<TOutput> WithArbiter(ILlmProvider arbiter)
    {
        _arbiter = arbiter;
        return this;
    }

    public DeliberationBuilder<TOutput> WithConvergenceCalculator(IConvergenceCalculator calculator)
    {
        _convergenceCalculator = calculator;
        return this;
    }

    public DeliberationBuilder<TOutput> WithLlmConvergence(ILlmProvider provider, string? model = null)
    {
        _convergenceCalculator = new LlmConvergenceCalculator(provider, model);
        return this;
    }

    public DeliberationBuilder<TOutput> WithVotingContext(VotingContext context)
    {
        _votingContext = context;
        return this;
    }

    public DeliberationBuilder<TOutput> WithLogger(ILogger logger)
    {
        _logger = logger;
        return this;
    }

    public DeliberationBuilder<TOutput> WithContextManager(IContextManager contextManager)
    {
        _contextManager = contextManager;
        return this;
    }

    public DeliberationBuilder<TOutput> WithContextOptions(ContextWindowOptions options)
    {
        _contextOptions = options;
        return this;
    }

    public DeliberationBuilder<TOutput> WithSlidingWindow(SlidingWindowOptions? options = null)
    {
        _contextManager = new SlidingWindowContextManager(options);
        return this;
    }

    public DeliberationBuilder<TOutput> WithSlidingWindow(int maxTokens, int? maxMessages = null)
    {
        _contextManager = new SlidingWindowContextManager(new SlidingWindowOptions
        {
            MaxTokens = maxTokens,
            MaxMessages = maxMessages
        });
        return this;
    }

    public DeliberationBuilder<TOutput> WithRecursiveSummarization(
        ILlmProvider llmProvider,
        RecursiveSummarizationOptions? options = null)
    {
        _contextManager = new RecursiveSummarizationContextManager(llmProvider, options);
        return this;
    }

    public DeliberationBuilder<TOutput> WithRecursiveSummarization(
        ILlmProvider llmProvider,
        int maxTokenBudget,
        int preserveRecentRounds = 2)
    {
        _contextManager = new RecursiveSummarizationContextManager(llmProvider, new RecursiveSummarizationOptions
        {
            MaxTokenBudget = maxTokenBudget,
            PreserveRecentRounds = preserveRecentRounds
        });
        return this;
    }

    public DeliberationBuilder<TOutput> WithHierarchicalContext(
        ILlmProvider llmProvider,
        HierarchicalContextOptions? options = null)
    {
        _contextManager = new HierarchicalContextManager(llmProvider, options);
        return this;
    }

    public DeliberationBuilder<TOutput> WithHierarchicalContext(
        ILlmProvider llmProvider,
        int maxTokenBudget,
        int roundsPerPhase = 3)
    {
        _contextManager = new HierarchicalContextManager(llmProvider, new HierarchicalContextOptions
        {
            MaxTokenBudget = maxTokenBudget,
            RoundsPerPhase = roundsPerPhase
        });
        return this;
    }

    public DeliberationBuilder<TOutput> WithObservationMasking(ObservationMaskingOptions? options = null)
    {
        _contextManager = new ObservationMaskingContextManager(options);
        return this;
    }

    public DeliberationBuilder<TOutput> WithObservationMasking(
        MaskingStrategy strategy,
        int verbosityThreshold = 500)
    {
        _contextManager = new ObservationMaskingContextManager(new ObservationMaskingOptions
        {
            MaskingStrategy = strategy,
            VerbosityThreshold = verbosityThreshold
        });
        return this;
    }

    public DeliberationBuilder<TOutput> WithHybridContextManagement(
        ILlmProvider llmProvider,
        HybridContextOptions? options = null)
    {
        _contextManager = new HybridContextManager(llmProvider, options);
        return this;
    }

    public DeliberationBuilder<TOutput> WithHybridContextManagement(
        ILlmProvider llmProvider,
        int maxTokenBudget)
    {
        _contextManager = new HybridContextManager(llmProvider, new HybridContextOptions
        {
            MaxTokenBudget = maxTokenBudget
        });
        return this;
    }

    public DeliberationWorkflow<TOutput> Build()
    {
        if (_agents.Count == 0)
            throw new InvalidOperationException("At least one agent is required");

        _budget ??= new DeliberationBudget().WithMaxRounds(5);
        _finalVotingStrategy ??= new ConsensusVotingStrategy();

        if (_arbiter != null)
        {
            _votingContext = (_votingContext ?? new VotingContext()) with
            {
                ArbiterProvider = _arbiter
            };
        }

        return new DeliberationWorkflow<TOutput>(
            _name,
            _agents,
            _mode,
            _budget,
            _finalVotingStrategy,
            _convergenceCalculator,
            _moderator,
            _votingContext,
            _logger,
            _contextManager,
            _contextOptions);
    }
}

public static class Deliberation
{
    public static DeliberationBuilder<string> Create() => new();

    public static DeliberationBuilder<TOutput> Create<TOutput>() where TOutput : class => new();
}
