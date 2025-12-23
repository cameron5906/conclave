using Conclave.Abstractions;
using Conclave.Models;
using Conclave.Workflows;

namespace Conclave.Extensions.AspNetCore;

public interface IConclaveFactory
{
    ProviderRegistry Providers { get; }
    IReadOnlyList<IAgent> Agents { get; }

    ILlmProvider GetProvider(string key);
    WorkflowBuilder<string> CreateWorkflow();
    WorkflowBuilder<TOutput> CreateWorkflow<TOutput>() where TOutput : class;

    Task<TaskResult<string>> ExecuteAsync(
        string task,
        VotingStrategy strategy = VotingStrategy.Majority,
        WorkflowOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<TaskResult<TOutput>> ExecuteAsync<TOutput>(
        string task,
        VotingStrategy strategy = VotingStrategy.Majority,
        WorkflowOptions? options = null,
        CancellationToken cancellationToken = default) where TOutput : class;
}
