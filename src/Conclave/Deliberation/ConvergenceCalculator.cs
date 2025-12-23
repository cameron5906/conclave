using Conclave.Abstractions;
using Conclave.Models;

namespace Conclave.Deliberation;

public interface IConvergenceCalculator
{
    Task<double> CalculateConvergenceAsync(
        DeliberationState state,
        CancellationToken cancellationToken = default);
}

public class SimpleConvergenceCalculator : IConvergenceCalculator
{
    public Task<double> CalculateConvergenceAsync(
        DeliberationState state,
        CancellationToken cancellationToken = default)
    {
        if (state.CurrentRound < 2 || state.Transcript.Count < 2)
        {
            return Task.FromResult(0.0);
        }

        var lastRoundMessages = state.Transcript
            .Where(m => m.Round == state.CurrentRound)
            .ToList();

        var previousRoundMessages = state.Transcript
            .Where(m => m.Round == state.CurrentRound - 1)
            .ToList();

        if (lastRoundMessages.Count == 0 || previousRoundMessages.Count == 0)
        {
            return Task.FromResult(0.0);
        }

        double totalSimilarity = 0;
        int comparisons = 0;

        foreach (var current in lastRoundMessages)
        {
            var previous = previousRoundMessages
                .FirstOrDefault(m => m.AgentId == current.AgentId);

            if (previous != null)
            {
                totalSimilarity += CalculateTextSimilarity(current.Content, previous.Content);
                comparisons++;
            }
        }

        return Task.FromResult(comparisons > 0 ? totalSimilarity / comparisons : 0.0);
    }

    private double CalculateTextSimilarity(string a, string b)
    {
        var wordsA = TokenizeAndNormalize(a);
        var wordsB = TokenizeAndNormalize(b);

        if (wordsA.Count == 0 || wordsB.Count == 0)
        {
            return 0.0;
        }

        var intersection = wordsA.Intersect(wordsB).Count();
        var union = wordsA.Union(wordsB).Count();

        return union > 0 ? (double)intersection / union : 0.0;
    }

    private HashSet<string> TokenizeAndNormalize(string text)
    {
        return text
            .ToLowerInvariant()
            .Split(new[] { ' ', '\n', '\r', '\t', '.', ',', '!', '?', ';', ':', '-' },
                StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .ToHashSet();
    }
}

public class LlmConvergenceCalculator : IConvergenceCalculator
{
    private readonly ILlmProvider _provider;
    private readonly string? _model;

    public LlmConvergenceCalculator(ILlmProvider provider, string? model = null)
    {
        _provider = provider;
        _model = model;
    }

    public async Task<double> CalculateConvergenceAsync(
        DeliberationState state,
        CancellationToken cancellationToken = default)
    {
        if (state.CurrentRound < 2)
        {
            return 0.0;
        }

        var prompt = BuildConvergencePrompt(state);
        var messages = new List<Message>
        {
            Message.System("""
                You are analyzing a multi-agent deliberation to measure convergence.
                Respond with ONLY a number between 0.0 and 1.0 representing how converged
                the agents' positions are.

                0.0 = Complete disagreement, diverging positions
                0.5 = Partial agreement, some common ground
                0.8 = Strong agreement with minor differences
                1.0 = Complete consensus

                Respond with just the number, nothing else.
                """),
            Message.User(prompt)
        };

        var options = new LlmCompletionOptions
        {
            Model = _model,
            Temperature = 0.1,
            MaxTokens = 10
        };

        var response = await _provider.CompleteAsync(messages, options, cancellationToken);

        if (double.TryParse(response.Content.Trim(), out var score))
        {
            return Math.Clamp(score, 0.0, 1.0);
        }

        return 0.5;
    }

    private string BuildConvergencePrompt(DeliberationState state)
    {
        var lastTwoRounds = state.Transcript
            .Where(m => m.Round >= state.CurrentRound - 1)
            .OrderBy(m => m.Round)
            .ThenBy(m => m.AgentName);

        return $"""
            Task: {state.Task}

            Recent Discussion:
            {string.Join("\n\n", lastTwoRounds.Select(m =>
                $"[Round {m.Round}] {m.AgentName}: {m.Content}"))}

            Rate the convergence (0.0-1.0):
            """;
    }
}
