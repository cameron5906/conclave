using System.Diagnostics;
using Conclave.Abstractions;
using Conclave.Context;
using Conclave.Models;
using Microsoft.Extensions.Logging;

namespace Conclave.Deliberation;

public class DeliberationWorkflow<TOutput> where TOutput : class
{
    private readonly string _name;
    private readonly List<IAgent> _agents;
    private readonly DeliberationMode _mode;
    private readonly DeliberationBudget _budget;
    private readonly IVotingStrategy _finalVotingStrategy;
    private readonly IConvergenceCalculator _convergenceCalculator;
    private readonly IAgent? _moderator;
    private readonly ILogger? _logger;
    private readonly VotingContext _votingContext;
    private readonly IContextManager? _contextManager;
    private readonly ContextWindowOptions? _contextOptions;

    public DeliberationWorkflow(
        string name,
        IEnumerable<IAgent> agents,
        DeliberationMode mode,
        DeliberationBudget budget,
        IVotingStrategy finalVotingStrategy,
        IConvergenceCalculator? convergenceCalculator = null,
        IAgent? moderator = null,
        VotingContext? votingContext = null,
        ILogger? logger = null,
        IContextManager? contextManager = null,
        ContextWindowOptions? contextOptions = null)
    {
        _name = name;
        _agents = agents.ToList();
        _mode = mode;
        _budget = budget;
        _finalVotingStrategy = finalVotingStrategy;
        _convergenceCalculator = convergenceCalculator ?? new SimpleConvergenceCalculator();
        _moderator = moderator;
        _votingContext = votingContext ?? new VotingContext();
        _logger = logger;
        _contextManager = contextManager;
        _contextOptions = contextOptions;
    }

    public string Name => _name;
    public IReadOnlyList<IAgent> Agents => _agents.AsReadOnly();
    public DeliberationMode Mode => _mode;

    public async Task<DeliberationResult<TOutput>> ExecuteAsync(
        string task,
        DeliberationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DeliberationOptions();
        var stopwatch = Stopwatch.StartNew();

        var state = new DeliberationState
        {
            Task = task,
            CurrentRound = 0,
            ParticipatingAgentIds = _agents.Select(a => a.Id).ToList()
        };

        try
        {
            ReportProgress(options, DeliberationStage.Initializing, "Starting deliberation", state);

            var terminationStrategy = _budget.Build();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                state.ElapsedTime = stopwatch.Elapsed;

                var termination = await terminationStrategy.ShouldTerminateAsync(state, cancellationToken);
                if (termination.ShouldTerminate)
                {
                    _logger?.LogInformation(
                        "Deliberation terminating: {Reason} - {Explanation}",
                        termination.Reason,
                        termination.Explanation);

                    return await FinalizeResultAsync(
                        state,
                        termination.Reason,
                        options,
                        cancellationToken);
                }

                state.CurrentRound++;
                ReportProgress(options, DeliberationStage.RoundStarting,
                    $"Starting round {state.CurrentRound}", state);

                await ExecuteRoundAsync(state, task, options, cancellationToken);

                state.ConvergenceScore = await _convergenceCalculator.CalculateConvergenceAsync(
                    state, cancellationToken);

                ReportProgress(options, DeliberationStage.EvaluatingConvergence,
                    $"Convergence: {state.ConvergenceScore:P0}", state);
            }
        }
        catch (OperationCanceledException)
        {
            state.ElapsedTime = stopwatch.Elapsed;
            return DeliberationResult<TOutput>.Failure("Deliberation was cancelled", state);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Deliberation {Name} failed", _name);
            state.ElapsedTime = stopwatch.Elapsed;
            return DeliberationResult<TOutput>.Failure(ex.Message, state);
        }
    }

    private async Task ExecuteRoundAsync(
        DeliberationState state,
        string task,
        DeliberationOptions options,
        CancellationToken cancellationToken)
    {
        switch (_mode)
        {
            case DeliberationMode.RoundRobin:
                await ExecuteRoundRobinAsync(state, task, options, cancellationToken);
                break;
            case DeliberationMode.Debate:
                await ExecuteDebateAsync(state, task, options, cancellationToken);
                break;
            case DeliberationMode.Moderated:
                await ExecuteModeratedAsync(state, task, options, cancellationToken);
                break;
            case DeliberationMode.FreeForm:
                await ExecuteFreeFormAsync(state, task, options, cancellationToken);
                break;
        }
    }

    private async Task ExecuteRoundRobinAsync(
        DeliberationState state,
        string task,
        DeliberationOptions options,
        CancellationToken cancellationToken)
    {
        foreach (var agent in _agents)
        {
            state.CurrentSpeaker = agent.Id;
            ReportProgress(options, DeliberationStage.AgentSpeaking,
                $"{agent.Name} is speaking", state);

            var prompt = BuildAgentPrompt(agent, state, task);
            var context = _contextManager != null
                ? await BuildContextForAgentAsync(agent, state, cancellationToken)
                : BuildContextForAgent(agent, state);

            var response = await agent.ProcessAsync(prompt, context, cancellationToken);

            var message = new DeliberationMessage
            {
                AgentId = agent.Id,
                AgentName = agent.Name,
                Content = response.Response,
                Round = state.CurrentRound,
                TokenCount = EstimateTokens(response.Response)
            };

            state.Transcript.Add(message);
            state.TotalTokensUsed += message.TokenCount + EstimateTokens(prompt);

            TrackAgentPosition(state, agent.Id, response.Response);
        }

        ReportProgress(options, DeliberationStage.RoundComplete,
            $"Round {state.CurrentRound} complete", state);
    }

    private async Task ExecuteDebateAsync(
        DeliberationState state,
        string task,
        DeliberationOptions options,
        CancellationToken cancellationToken)
    {
        var agentTasks = _agents.Select(async agent =>
        {
            state.CurrentSpeaker = agent.Id;

            var opponents = state.Transcript
                .Where(m => m.AgentId != agent.Id && m.Round == state.CurrentRound - 1)
                .ToList();

            var prompt = BuildDebatePrompt(agent, state, task, opponents);
            var context = _contextManager != null
                ? await BuildContextForAgentAsync(agent, state, cancellationToken)
                : BuildContextForAgent(agent, state);

            var response = await agent.ProcessAsync(prompt, context, cancellationToken);

            return new DeliberationMessage
            {
                AgentId = agent.Id,
                AgentName = agent.Name,
                Content = response.Response,
                Round = state.CurrentRound,
                TokenCount = EstimateTokens(response.Response),
                InResponseTo = opponents.FirstOrDefault()?.AgentId
            };
        });

        var messages = await Task.WhenAll(agentTasks);

        foreach (var message in messages)
        {
            state.Transcript.Add(message);
            state.TotalTokensUsed += message.TokenCount;
            TrackAgentPosition(state, message.AgentId, message.Content);
        }

        ReportProgress(options, DeliberationStage.RoundComplete,
            $"Debate round {state.CurrentRound} complete", state);
    }

    private async Task ExecuteModeratedAsync(
        DeliberationState state,
        string task,
        DeliberationOptions options,
        CancellationToken cancellationToken)
    {
        if (_moderator == null)
        {
            await ExecuteRoundRobinAsync(state, task, options, cancellationToken);
            return;
        }

        var moderatorPrompt = BuildModeratorPrompt(state, task);
        var moderatorResponse = await _moderator.ProcessAsync(
            moderatorPrompt,
            null,
            cancellationToken);

        state.Transcript.Add(new DeliberationMessage
        {
            AgentId = _moderator.Id,
            AgentName = $"[Moderator] {_moderator.Name}",
            Content = moderatorResponse.Response,
            Round = state.CurrentRound,
            TokenCount = EstimateTokens(moderatorResponse.Response)
        });

        foreach (var agent in _agents)
        {
            if (agent.Id == _moderator.Id) continue;

            state.CurrentSpeaker = agent.Id;
            ReportProgress(options, DeliberationStage.AgentSpeaking,
                $"{agent.Name} responding to moderator", state);

            var prompt = BuildModeratedAgentPrompt(agent, state, task, moderatorResponse.Response);
            var context = _contextManager != null
                ? await BuildContextForAgentAsync(agent, state, cancellationToken)
                : BuildContextForAgent(agent, state);

            var response = await agent.ProcessAsync(prompt, context, cancellationToken);

            var message = new DeliberationMessage
            {
                AgentId = agent.Id,
                AgentName = agent.Name,
                Content = response.Response,
                Round = state.CurrentRound,
                TokenCount = EstimateTokens(response.Response),
                InResponseTo = _moderator.Id
            };

            state.Transcript.Add(message);
            state.TotalTokensUsed += message.TokenCount;
            TrackAgentPosition(state, agent.Id, response.Response);
        }

        ReportProgress(options, DeliberationStage.RoundComplete,
            $"Moderated round {state.CurrentRound} complete", state);
    }

    private async Task ExecuteFreeFormAsync(
        DeliberationState state,
        string task,
        DeliberationOptions options,
        CancellationToken cancellationToken)
    {
        var agentTasks = _agents.Select(async agent =>
        {
            state.CurrentSpeaker = agent.Id;

            var prompt = BuildFreeFormPrompt(agent, state, task);
            var context = _contextManager != null
                ? await BuildContextForAgentAsync(agent, state, cancellationToken)
                : BuildContextForAgent(agent, state);

            var response = await agent.ProcessAsync(prompt, context, cancellationToken);

            return new DeliberationMessage
            {
                AgentId = agent.Id,
                AgentName = agent.Name,
                Content = response.Response,
                Round = state.CurrentRound,
                TokenCount = EstimateTokens(response.Response)
            };
        });

        var messages = await Task.WhenAll(agentTasks);

        foreach (var message in messages)
        {
            state.Transcript.Add(message);
            state.TotalTokensUsed += message.TokenCount;
            TrackAgentPosition(state, message.AgentId, message.Content);
        }

        ReportProgress(options, DeliberationStage.RoundComplete,
            $"Round {state.CurrentRound} complete", state);
    }

    private string BuildAgentPrompt(IAgent agent, DeliberationState state, string task)
    {
        if (state.CurrentRound == 1)
        {
            return $"""
                You are participating in a multi-agent deliberation.

                **Task:** {task}

                Please provide your initial perspective on this task. Be thorough but concise.
                """;
        }

        var recentMessages = state.Transcript
            .Where(m => m.Round == state.CurrentRound - 1 && m.AgentId != agent.Id)
            .ToList();

        return $"""
            You are participating in a multi-agent deliberation (Round {state.CurrentRound}).

            **Task:** {task}

            **Recent contributions from other agents:**
            {string.Join("\n\n", recentMessages.Select(m => $"{m.AgentName}: {m.Content}"))}

            Consider the other perspectives and refine your position. You may:
            - Agree with points that are well-reasoned
            - Respectfully disagree with points you find flawed
            - Introduce new considerations
            - Synthesize ideas from multiple sources

            Provide your updated perspective:
            """;
    }

    private string BuildDebatePrompt(
        IAgent agent,
        DeliberationState state,
        string task,
        List<DeliberationMessage> opponents)
    {
        if (opponents.Count == 0)
        {
            return BuildAgentPrompt(agent, state, task);
        }

        return $"""
            You are in a structured debate (Round {state.CurrentRound}).

            **Task:** {task}

            **Arguments to address:**
            {string.Join("\n\n", opponents.Select(m => $"{m.AgentName} argued:\n{m.Content}"))}

            Engage directly with these arguments. Challenge weak points and acknowledge strong ones.
            Present your refined position:
            """;
    }

    private string BuildModeratorPrompt(DeliberationState state, string task)
    {
        var lastRound = state.Transcript
            .Where(m => m.Round == state.CurrentRound - 1)
            .ToList();

        if (lastRound.Count == 0)
        {
            return $"""
                You are moderating a multi-agent deliberation.

                **Task:** {task}

                This is the first round. Frame the discussion by:
                1. Clarifying the key questions to address
                2. Suggesting how to approach the problem
                3. Setting expectations for the discussion

                Your opening:
                """;
        }

        return $"""
            You are moderating a multi-agent deliberation (Round {state.CurrentRound}).

            **Task:** {task}

            **Previous round contributions:**
            {string.Join("\n\n", lastRound.Select(m => $"{m.AgentName}: {m.Content}"))}

            As moderator:
            1. Summarize key points of agreement and disagreement
            2. Identify promising directions to explore
            3. Pose focused questions for the next round

            Your moderation:
            """;
    }

    private string BuildModeratedAgentPrompt(
        IAgent agent,
        DeliberationState state,
        string task,
        string moderatorGuidance)
    {
        return $"""
            You are participating in a moderated deliberation (Round {state.CurrentRound}).

            **Task:** {task}

            **Moderator's guidance:**
            {moderatorGuidance}

            Respond to the moderator's questions and continue developing your position:
            """;
    }

    private string BuildFreeFormPrompt(IAgent agent, DeliberationState state, string task)
    {
        var allMessages = state.Transcript
            .OrderBy(m => m.Round)
            .ThenBy(m => m.Timestamp)
            .ToList();

        return $"""
            You are in a free-form multi-agent discussion (Round {state.CurrentRound}).

            **Task:** {task}

            **Discussion so far:**
            {(allMessages.Any() ? string.Join("\n\n", allMessages.Select(m => $"[R{m.Round}] {m.AgentName}: {m.Content}")) : "(This is the first round)")}

            Contribute to the discussion naturally. Build on others' ideas or introduce new perspectives:
            """;
    }

    private List<Message> BuildContextForAgent(IAgent agent, DeliberationState state)
    {
        var context = new List<Message>
        {
            Message.System($"""
                You are {agent.Name}. {agent.Personality.Description}
                {agent.Personality.SystemPrompt}

                You are participating in a multi-agent deliberation with these participants:
                {string.Join(", ", _agents.Where(a => a.Id != agent.Id).Select(a => a.Name))}

                Be collaborative but maintain your unique perspective based on your expertise.
                """)
        };

        return context;
    }

    private async Task<List<Message>> BuildContextForAgentAsync(
        IAgent agent,
        DeliberationState state,
        CancellationToken cancellationToken)
    {
        var baseContext = BuildContextForAgent(agent, state);

        if (_contextManager == null || state.Transcript.Count == 0)
        {
            return baseContext;
        }

        var contextWindow = await _contextManager.GetContextWindowAsync(
            state,
            agent.Id,
            _contextOptions,
            cancellationToken);

        if (!string.IsNullOrEmpty(contextWindow.Summary))
        {
            baseContext.Add(Message.System($"[Context Summary]\n{contextWindow.Summary}"));
        }

        baseContext.AddRange(contextWindow.Messages);

        _logger?.LogDebug(
            "Context for {Agent}: {Original} messages -> {Retained} messages ({Compression:P0} compression)",
            agent.Name,
            contextWindow.OriginalMessageCount,
            contextWindow.RetainedMessageCount,
            contextWindow.CompressionRatio);

        return baseContext;
    }

    private async Task<DeliberationResult<TOutput>> FinalizeResultAsync(
        DeliberationState state,
        TerminationReason reason,
        DeliberationOptions options,
        CancellationToken cancellationToken)
    {
        ReportProgress(options, DeliberationStage.Synthesizing,
            "Synthesizing final result", state);

        var lastRoundResponses = state.Transcript
            .Where(m => m.Round == state.CurrentRound)
            .Select(m => new AgentResponse
            {
                AgentId = m.AgentId,
                AgentName = m.AgentName,
                Response = m.Content
            })
            .ToList();

        if (lastRoundResponses.Count == 0)
        {
            lastRoundResponses = state.Transcript
                .GroupBy(m => m.AgentId)
                .Select(g => g.OrderByDescending(m => m.Round).First())
                .Select(m => new AgentResponse
                {
                    AgentId = m.AgentId,
                    AgentName = m.AgentName,
                    Response = m.Content
                })
                .ToList();
        }

        var synthesisTask = $"""
            Synthesize the final conclusion from this multi-agent deliberation.

            **Original Task:** {state.Task}

            **Final Positions:**
            {string.Join("\n\n", lastRoundResponses.Select(r => $"{r.AgentName}: {r.Response}"))}

            **Deliberation Statistics:**
            - Rounds: {state.CurrentRound}
            - Final Convergence: {state.ConvergenceScore:P0}

            Provide a unified response that represents the group's conclusion:
            """;

        var votingResult = await _finalVotingStrategy.EvaluateAsync(
            synthesisTask,
            lastRoundResponses,
            _votingContext,
            cancellationToken);

        ReportProgress(options, DeliberationStage.Complete,
            "Deliberation complete", state);

        var output = ExtractOutput(votingResult);

        return new DeliberationResult<TOutput>
        {
            IsSuccess = output != null,
            Value = output,
            State = state,
            TerminationReason = reason,
            TotalRounds = state.CurrentRound,
            TotalTokens = state.TotalTokensUsed,
            TotalTime = state.ElapsedTime,
            FinalConvergenceScore = state.ConvergenceScore ?? 0
        };
    }

    private TOutput? ExtractOutput(VotingResult votingResult)
    {
        if (typeof(TOutput) == typeof(string))
        {
            return votingResult.WinningResponse as TOutput;
        }

        if (votingResult.WinningStructuredOutput is TOutput structured)
        {
            return structured;
        }

        return default;
    }

    private void TrackAgentPosition(DeliberationState state, string agentId, string content)
    {
        if (!state.AgentPositions.ContainsKey(agentId))
        {
            state.AgentPositions[agentId] = new List<string>();
        }
        state.AgentPositions[agentId].Add(content);
    }

    private int EstimateTokens(string text)
    {
        return (int)(text.Length / 4.0);
    }

    private void ReportProgress(
        DeliberationOptions options,
        DeliberationStage stage,
        string message,
        DeliberationState state)
    {
        _logger?.LogDebug("Deliberation {Name} - {Stage}: {Message}", _name, stage, message);

        options.OnProgress?.Invoke(new DeliberationProgress
        {
            Stage = stage,
            Message = message,
            CurrentRound = state.CurrentRound,
            MaxRounds = _budget.MaxRounds,
            CurrentSpeaker = state.CurrentSpeaker ?? string.Empty,
            TokensUsed = state.TotalTokensUsed,
            TokenBudget = _budget.MaxTokens,
            ElapsedTime = state.ElapsedTime,
            TimeBudget = _budget.MaxTime,
            ConvergenceScore = state.ConvergenceScore,
            ConvergenceThreshold = _budget.ConvergenceThreshold
        });
    }
}

public class DeliberationOptions
{
    public Action<DeliberationProgress>? OnProgress { get; init; }
    public IReadOnlyList<Message>? InitialContext { get; init; }
}
