using System.Diagnostics;
using Conclave.Abstractions;
using Conclave.Models;
using Microsoft.Extensions.Logging;

namespace Conclave.Workflows;

public class ConclaveWorkflow<TOutput> : IWorkflow<TOutput> where TOutput : class
{
    private readonly ILogger? _logger;
    private readonly List<IAgent> _agents;
    private readonly VotingContext _votingContext;

    public string Name { get; }
    public IReadOnlyList<IAgent> Agents => _agents.AsReadOnly();
    public IVotingStrategy VotingStrategy { get; }

    public ConclaveWorkflow(
        string name,
        IEnumerable<IAgent> agents,
        IVotingStrategy votingStrategy,
        VotingContext? votingContext = null,
        ILogger? logger = null)
    {
        Name = name;
        _agents = agents.ToList();
        VotingStrategy = votingStrategy;
        _votingContext = votingContext ?? new VotingContext();
        _logger = logger;
    }

    public async Task<TaskResult<TOutput>> ExecuteAsync(
        string task,
        WorkflowOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new WorkflowOptions();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            ReportProgress(options, WorkflowStage.Initializing, "Starting workflow execution");

            var responses = await CollectAgentResponsesAsync(task, options, cancellationToken);

            if (!responses.Any())
            {
                return TaskResult<TOutput>.Failure("No agent responses received");
            }

            ReportProgress(options, WorkflowStage.Voting, "Evaluating responses");

            var votingResult = await VotingStrategy.EvaluateAsync(
                task, responses, _votingContext, cancellationToken);

            if (options.RequireConsensus && votingResult.ConsensusScore < options.MinimumConsensusScore)
            {
                ReportProgress(options, WorkflowStage.ConsensusBuilding, "Attempting to build consensus");
                votingResult = await AttemptConsensusAsync(task, responses, options, cancellationToken);
            }

            ReportProgress(options, WorkflowStage.Finalizing, "Finalizing result");

            var output = ExtractOutput(votingResult);
            stopwatch.Stop();

            ReportProgress(options, WorkflowStage.Completed, "Workflow completed successfully");

            return new TaskResult<TOutput>
            {
                IsSuccess = true,
                Value = output,
                AgentResponses = responses,
                VotingResult = votingResult,
                ExecutionTime = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return TaskResult<TOutput>.Failure("Workflow was cancelled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Workflow {WorkflowName} failed", Name);
            stopwatch.Stop();
            ReportProgress(options, WorkflowStage.Failed, $"Workflow failed: {ex.Message}");
            return TaskResult<TOutput>.Failure(ex.Message);
        }
    }

    private async Task<List<AgentResponse>> CollectAgentResponsesAsync(
        string task,
        WorkflowOptions options,
        CancellationToken cancellationToken)
    {
        var responses = new List<AgentResponse>();
        var completed = 0;

        if (options.EnableParallelExecution)
        {
            var tasks = _agents.Select(async agent =>
            {
                ReportProgress(options, WorkflowStage.AgentProcessing,
                    $"Agent {agent.Name} processing", completed, _agents.Count, agent.Id);

                var response = typeof(TOutput) == typeof(string)
                    ? await agent.ProcessAsync(task, options.InitialContext, cancellationToken)
                    : await agent.ProcessWithStructuredOutputAsync<TOutput>(task, options.InitialContext, cancellationToken);

                Interlocked.Increment(ref completed);
                ReportProgress(options, WorkflowStage.AgentProcessing,
                    $"Agent {agent.Name} completed", completed, _agents.Count, agent.Id);

                return response;
            });

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(options.Timeout);

            try
            {
                responses = (await Task.WhenAll(tasks)).ToList();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Workflow timed out after {options.Timeout}");
            }
        }
        else
        {
            foreach (var agent in _agents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ReportProgress(options, WorkflowStage.AgentProcessing,
                    $"Agent {agent.Name} processing", completed, _agents.Count, agent.Id);

                var response = typeof(TOutput) == typeof(string)
                    ? await agent.ProcessAsync(task, options.InitialContext, cancellationToken)
                    : await agent.ProcessWithStructuredOutputAsync<TOutput>(task, options.InitialContext, cancellationToken);

                responses.Add(response);
                completed++;

                ReportProgress(options, WorkflowStage.AgentProcessing,
                    $"Agent {agent.Name} completed", completed, _agents.Count, agent.Id);
            }
        }

        return responses;
    }

    private async Task<VotingResult> AttemptConsensusAsync(
        string task,
        IReadOnlyList<AgentResponse> responses,
        WorkflowOptions options,
        CancellationToken cancellationToken)
    {
        var consensusStrategy = new Voting.ConsensusVotingStrategy();
        return await consensusStrategy.EvaluateAsync(task, responses, _votingContext, cancellationToken);
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

    private void ReportProgress(
        WorkflowOptions options,
        WorkflowStage stage,
        string message,
        int completed = 0,
        int total = 0,
        string? agentId = null)
    {
        _logger?.LogDebug("Workflow {Name} - {Stage}: {Message}", Name, stage, message);

        options.OnProgress?.Invoke(new WorkflowProgress
        {
            Stage = stage,
            Message = message,
            CompletedAgents = completed,
            TotalAgents = total > 0 ? total : _agents.Count,
            CurrentAgentId = agentId
        });
    }
}
