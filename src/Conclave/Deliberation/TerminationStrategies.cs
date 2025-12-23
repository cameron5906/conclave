namespace Conclave.Deliberation;

public class MaxRoundsTermination : ITerminationStrategy
{
    private readonly int _maxRounds;

    public MaxRoundsTermination(int maxRounds)
    {
        _maxRounds = maxRounds;
    }

    public string Name => "MaxRounds";

    public Task<TerminationDecision> ShouldTerminateAsync(
        DeliberationState state,
        CancellationToken cancellationToken = default)
    {
        if (state.CurrentRound >= _maxRounds)
        {
            return Task.FromResult(TerminationDecision.Terminate(
                TerminationReason.MaxRoundsReached,
                $"Reached maximum of {_maxRounds} rounds"));
        }

        return Task.FromResult(TerminationDecision.Continue());
    }
}

public class MaxTokensTermination : ITerminationStrategy
{
    private readonly int _maxTokens;

    public MaxTokensTermination(int maxTokens)
    {
        _maxTokens = maxTokens;
    }

    public string Name => "MaxTokens";

    public Task<TerminationDecision> ShouldTerminateAsync(
        DeliberationState state,
        CancellationToken cancellationToken = default)
    {
        if (state.TotalTokensUsed >= _maxTokens)
        {
            return Task.FromResult(TerminationDecision.Terminate(
                TerminationReason.MaxTokensReached,
                $"Exceeded token budget of {_maxTokens} (used: {state.TotalTokensUsed})"));
        }

        return Task.FromResult(TerminationDecision.Continue());
    }
}

public class MaxTimeTermination : ITerminationStrategy
{
    private readonly TimeSpan _maxTime;

    public MaxTimeTermination(TimeSpan maxTime)
    {
        _maxTime = maxTime;
    }

    public string Name => "MaxTime";

    public Task<TerminationDecision> ShouldTerminateAsync(
        DeliberationState state,
        CancellationToken cancellationToken = default)
    {
        if (state.ElapsedTime >= _maxTime)
        {
            return Task.FromResult(TerminationDecision.Terminate(
                TerminationReason.MaxTimeReached,
                $"Exceeded time limit of {_maxTime.TotalSeconds:F1}s"));
        }

        return Task.FromResult(TerminationDecision.Continue());
    }
}

public class ConvergenceTermination : ITerminationStrategy
{
    private readonly double _threshold;
    private readonly int _minRounds;

    public ConvergenceTermination(double threshold, int minRounds = 2)
    {
        _threshold = threshold;
        _minRounds = minRounds;
    }

    public string Name => "Convergence";

    public Task<TerminationDecision> ShouldTerminateAsync(
        DeliberationState state,
        CancellationToken cancellationToken = default)
    {
        if (state.CurrentRound < _minRounds)
        {
            return Task.FromResult(TerminationDecision.Continue());
        }

        if (state.ConvergenceScore.HasValue && state.ConvergenceScore >= _threshold)
        {
            return Task.FromResult(TerminationDecision.Terminate(
                TerminationReason.ConvergenceAchieved,
                $"Convergence score {state.ConvergenceScore:P0} meets threshold {_threshold:P0}"));
        }

        return Task.FromResult(TerminationDecision.Continue());
    }
}

public class CustomTermination : ITerminationStrategy
{
    private readonly Func<DeliberationState, Task<bool>> _condition;
    private readonly string _description;

    public CustomTermination(
        Func<DeliberationState, Task<bool>> condition,
        string description = "Custom condition")
    {
        _condition = condition;
        _description = description;
    }

    public CustomTermination(
        Func<DeliberationState, bool> condition,
        string description = "Custom condition")
    {
        _condition = state => Task.FromResult(condition(state));
        _description = description;
    }

    public string Name => "Custom";

    public async Task<TerminationDecision> ShouldTerminateAsync(
        DeliberationState state,
        CancellationToken cancellationToken = default)
    {
        var shouldTerminate = await _condition(state);
        if (shouldTerminate)
        {
            return TerminationDecision.Terminate(
                TerminationReason.CustomCondition,
                _description);
        }

        return TerminationDecision.Continue();
    }
}

public class CompositeTermination : ITerminationStrategy
{
    private readonly List<ITerminationStrategy> _strategies;
    private readonly CompositeMode _mode;

    public enum CompositeMode
    {
        Any,
        All
    }

    public CompositeTermination(CompositeMode mode = CompositeMode.Any)
    {
        _strategies = new List<ITerminationStrategy>();
        _mode = mode;
    }

    public string Name => $"Composite({_mode})";

    public CompositeTermination Add(ITerminationStrategy strategy)
    {
        _strategies.Add(strategy);
        return this;
    }

    public async Task<TerminationDecision> ShouldTerminateAsync(
        DeliberationState state,
        CancellationToken cancellationToken = default)
    {
        var decisions = new List<TerminationDecision>();

        foreach (var strategy in _strategies)
        {
            var decision = await strategy.ShouldTerminateAsync(state, cancellationToken);
            decisions.Add(decision);

            if (_mode == CompositeMode.Any && decision.ShouldTerminate)
            {
                return decision;
            }
        }

        if (_mode == CompositeMode.All && decisions.All(d => d.ShouldTerminate))
        {
            var reasons = string.Join(", ", decisions
                .Where(d => d.ShouldTerminate)
                .Select(d => d.Explanation ?? d.Reason.ToString()));

            return TerminationDecision.Terminate(
                TerminationReason.CustomCondition,
                $"All conditions met: {reasons}");
        }

        return TerminationDecision.Continue();
    }
}
