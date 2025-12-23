using Conclave.Abstractions;

namespace Conclave.Deliberation;

public class DeliberationBudget
{
    private readonly CompositeTermination _termination;
    private int? _maxRounds;
    private int? _maxTokens;
    private TimeSpan? _maxTime;
    private double? _convergenceThreshold;
    private int _minRoundsForConvergence = 2;

    public DeliberationBudget()
    {
        _termination = new CompositeTermination(CompositeTermination.CompositeMode.Any);
    }

    public int? MaxRounds => _maxRounds;
    public int? MaxTokens => _maxTokens;
    public TimeSpan? MaxTime => _maxTime;
    public double? ConvergenceThreshold => _convergenceThreshold;

    public ITerminationStrategy Build() => _termination;

    public DeliberationBudget WithMaxRounds(int rounds)
    {
        _maxRounds = rounds;
        _termination.Add(new MaxRoundsTermination(rounds));
        return this;
    }

    public DeliberationBudget WithMaxTokens(int tokens)
    {
        _maxTokens = tokens;
        _termination.Add(new MaxTokensTermination(tokens));
        return this;
    }

    public DeliberationBudget WithMaxTime(TimeSpan time)
    {
        _maxTime = time;
        _termination.Add(new MaxTimeTermination(time));
        return this;
    }

    public DeliberationBudget WithConvergenceThreshold(double threshold, int minRounds = 2)
    {
        _convergenceThreshold = threshold;
        _minRoundsForConvergence = minRounds;
        _termination.Add(new ConvergenceTermination(threshold, minRounds));
        return this;
    }

    public DeliberationBudget WithAgentTerminator(
        IAgent agent,
        string? customPrompt = null,
        double confidenceThreshold = 0.7)
    {
        _termination.Add(new AgentTerminator(agent, customPrompt, confidenceThreshold));
        return this;
    }

    public DeliberationBudget WithWorkflowTerminator(
        IWorkflow<TerminatorResponse> workflow,
        double confidenceThreshold = 0.7)
    {
        _termination.Add(new WorkflowTerminator(workflow, confidenceThreshold));
        return this;
    }

    public DeliberationBudget WithCustomCondition(
        Func<DeliberationState, bool> condition,
        string description = "Custom termination condition")
    {
        _termination.Add(new CustomTermination(condition, description));
        return this;
    }

    public DeliberationBudget WithCustomConditionAsync(
        Func<DeliberationState, Task<bool>> condition,
        string description = "Custom async termination condition")
    {
        _termination.Add(new CustomTermination(condition, description));
        return this;
    }

    public DeliberationBudget WithTerminationStrategy(ITerminationStrategy strategy)
    {
        _termination.Add(strategy);
        return this;
    }
}

public class DeliberationBudgetBuilder
{
    private readonly DeliberationBudget _budget = new();

    public DeliberationBudgetBuilder MaxRounds(int rounds)
    {
        _budget.WithMaxRounds(rounds);
        return this;
    }

    public DeliberationBudgetBuilder MaxTokens(int tokens)
    {
        _budget.WithMaxTokens(tokens);
        return this;
    }

    public DeliberationBudgetBuilder MaxTime(TimeSpan time)
    {
        _budget.WithMaxTime(time);
        return this;
    }

    public DeliberationBudgetBuilder ConvergenceThreshold(double threshold, int minRounds = 2)
    {
        _budget.WithConvergenceThreshold(threshold, minRounds);
        return this;
    }

    public DeliberationBudgetBuilder AgentJudge(
        IAgent agent,
        string? customPrompt = null,
        double confidenceThreshold = 0.7)
    {
        _budget.WithAgentTerminator(agent, customPrompt, confidenceThreshold);
        return this;
    }

    public DeliberationBudgetBuilder WorkflowJudge(
        IWorkflow<TerminatorResponse> workflow,
        double confidenceThreshold = 0.7)
    {
        _budget.WithWorkflowTerminator(workflow, confidenceThreshold);
        return this;
    }

    public DeliberationBudgetBuilder When(
        Func<DeliberationState, bool> condition,
        string description = "Custom condition")
    {
        _budget.WithCustomCondition(condition, description);
        return this;
    }

    public DeliberationBudgetBuilder WhenAsync(
        Func<DeliberationState, Task<bool>> condition,
        string description = "Custom async condition")
    {
        _budget.WithCustomConditionAsync(condition, description);
        return this;
    }

    public DeliberationBudget Build() => _budget;
}
