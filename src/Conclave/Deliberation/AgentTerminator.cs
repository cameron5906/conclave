using System.Text.Json;
using Conclave.Abstractions;
using Conclave.Models;

namespace Conclave.Deliberation;

public class AgentTerminator : ITerminationStrategy
{
    private readonly IAgent _agent;
    private readonly string? _customPrompt;
    private readonly double _confidenceThreshold;

    public AgentTerminator(IAgent agent, string? customPrompt = null, double confidenceThreshold = 0.7)
    {
        _agent = agent;
        _customPrompt = customPrompt;
        _confidenceThreshold = confidenceThreshold;
    }

    public string Name => $"Agent({_agent.Name})";

    public async Task<TerminationDecision> ShouldTerminateAsync(
        DeliberationState state,
        CancellationToken cancellationToken = default)
    {
        var prompt = BuildPrompt(state);
        var context = new List<Message>
        {
            Message.System(GetSystemPrompt())
        };

        var response = await _agent.ProcessWithStructuredOutputAsync<TerminatorResponse>(
            prompt,
            context,
            cancellationToken);

        if (response.StructuredOutput is not TerminatorResponse result)
        {
            return TerminationDecision.Continue();
        }

        if (result.ShouldTerminate && result.Confidence >= _confidenceThreshold)
        {
            return TerminationDecision.TerminateWithConfidence(
                TerminationReason.AgentDecision,
                result.Confidence,
                result.Reasoning);
        }

        return TerminationDecision.Continue();
    }

    private string GetSystemPrompt()
    {
        return _customPrompt ?? """
            You are a deliberation termination judge. Your role is to analyze the current state of
            a multi-agent deliberation and determine if the discussion has reached a satisfactory
            conclusion or should continue.

            Consider:
            1. Have the agents reached consensus on the key points?
            2. Are the agents repeating themselves or going in circles?
            3. Has enough meaningful progress been made?
            4. Is further discussion likely to improve the outcome?
            5. Are there still significant unresolved disagreements that need attention?

            Provide your decision with confidence level and reasoning.
            """;
    }

    private string BuildPrompt(DeliberationState state)
    {
        return $"""
            ## Deliberation Status

            **Task:** {state.Task}
            **Current Round:** {state.CurrentRound}
            **Total Tokens Used:** {state.TotalTokensUsed}
            **Elapsed Time:** {state.ElapsedTime.TotalSeconds:F1}s
            **Current Convergence Score:** {state.ConvergenceScore?.ToString("P0") ?? "Not calculated"}

            ## Transcript

            {state.GetFormattedTranscript()}

            ## Your Decision

            Based on the above, should this deliberation terminate now?
            """;
    }
}

public class TerminatorResponse
{
    public bool ShouldTerminate { get; set; }
    public double Confidence { get; set; }
    public string Reasoning { get; set; } = string.Empty;
    public List<string> KeyPointsResolved { get; set; } = new();
    public List<string> OutstandingIssues { get; set; } = new();
}

public class WorkflowTerminator : ITerminationStrategy
{
    private readonly IWorkflow<TerminatorResponse> _workflow;
    private readonly double _confidenceThreshold;

    public WorkflowTerminator(IWorkflow<TerminatorResponse> workflow, double confidenceThreshold = 0.7)
    {
        _workflow = workflow;
        _confidenceThreshold = confidenceThreshold;
    }

    public string Name => $"Workflow({_workflow.Name})";

    public async Task<TerminationDecision> ShouldTerminateAsync(
        DeliberationState state,
        CancellationToken cancellationToken = default)
    {
        var task = BuildEvaluationTask(state);
        var result = await _workflow.ExecuteAsync(task, cancellationToken: cancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            return TerminationDecision.Continue();
        }

        var decision = result.Value;
        if (decision.ShouldTerminate && decision.Confidence >= _confidenceThreshold)
        {
            return TerminationDecision.TerminateWithConfidence(
                TerminationReason.WorkflowDecision,
                decision.Confidence,
                decision.Reasoning);
        }

        return TerminationDecision.Continue();
    }

    private string BuildEvaluationTask(DeliberationState state)
    {
        return $"""
            Evaluate whether this multi-agent deliberation should terminate.

            **Task Being Deliberated:** {state.Task}

            **Statistics:**
            - Rounds completed: {state.CurrentRound}
            - Tokens used: {state.TotalTokensUsed}
            - Time elapsed: {state.ElapsedTime.TotalSeconds:F1}s
            - Convergence score: {state.ConvergenceScore?.ToString("P0") ?? "N/A"}

            **Full Transcript:**
            {state.GetFormattedTranscript()}

            Analyze the discussion and determine if continuing would likely improve the outcome.
            """;
    }
}
