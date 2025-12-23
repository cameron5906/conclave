using Conclave.Abstractions;
using Conclave.Deliberation;
using Conclave.Models;

namespace Conclave.Context;

public abstract class BaseContextManager : IContextManager
{
    protected readonly ILlmProvider? LlmProvider;

    protected BaseContextManager(ILlmProvider? llmProvider = null)
    {
        LlmProvider = llmProvider;
    }

    public abstract ContextManagerType Type { get; }

    public abstract Task<ContextWindow> GetContextWindowAsync(
        DeliberationState state,
        string? agentId = null,
        ContextWindowOptions? options = null,
        CancellationToken cancellationToken = default);

    public virtual async Task<string> SummarizeAsync(
        IReadOnlyList<DeliberationMessage> messages,
        SummarizationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
            return string.Empty;

        if (LlmProvider == null)
            return CreateFallbackSummary(messages, options);

        var prompt = BuildSummarizationPrompt(messages, options);
        var response = await LlmProvider.CompleteAsync(
            [Message.User(prompt)],
            new LlmCompletionOptions { Temperature = 0.3 },
            cancellationToken);

        return response.Content;
    }

    protected virtual string BuildSummarizationPrompt(
        IReadOnlyList<DeliberationMessage> messages,
        SummarizationOptions? options)
    {
        var style = options?.Style ?? SummarizationStyle.Concise;
        var styleInstruction = style switch
        {
            SummarizationStyle.Concise => "Be concise and focus on key points only.",
            SummarizationStyle.Detailed => "Provide a detailed summary preserving important nuances.",
            SummarizationStyle.BulletPoints => "Format as bullet points, one per key point.",
            SummarizationStyle.Narrative => "Write as a flowing narrative summary.",
            _ => "Be concise."
        };

        var preserveInstructions = new List<string>();
        if (options?.PreserveKeyDecisions == true)
            preserveInstructions.Add("key decisions made");
        if (options?.PreserveDisagreements == true)
            preserveInstructions.Add("points of disagreement");

        var preserveText = preserveInstructions.Count > 0
            ? $"Ensure you preserve: {string.Join(", ", preserveInstructions)}."
            : "";

        var transcript = FormatMessagesForSummary(messages);

        return $"""
            Summarize the following multi-agent deliberation transcript.
            {styleInstruction}
            {preserveText}

            Transcript:
            {transcript}

            Summary:
            """;
    }

    protected virtual string CreateFallbackSummary(
        IReadOnlyList<DeliberationMessage> messages,
        SummarizationOptions? options)
    {
        var agentSummaries = messages
            .GroupBy(m => m.AgentName)
            .Select(g => $"{g.Key}: {g.Count()} messages")
            .ToList();

        var rounds = messages.Select(m => m.Round).Distinct().OrderBy(r => r).ToList();
        var roundRange = rounds.Count > 0 ? $"Rounds {rounds.First()}-{rounds.Last()}" : "No rounds";

        return $"[Summary of {messages.Count} messages across {roundRange}. Participants: {string.Join(", ", agentSummaries)}]";
    }

    protected string FormatMessagesForSummary(IReadOnlyList<DeliberationMessage> messages)
    {
        return string.Join("\n\n", messages.Select(m =>
            $"[Round {m.Round}] {m.AgentName}:\n{m.Content}"));
    }

    protected int EstimateTokens(string text)
    {
        return (int)(text.Length / 4.0);
    }

    protected int EstimateTokens(IReadOnlyList<DeliberationMessage> messages)
    {
        return messages.Sum(m => m.TokenCount > 0 ? m.TokenCount : EstimateTokens(m.Content));
    }

    protected Message ConvertToMessage(DeliberationMessage dm)
    {
        return Message.Assistant(dm.Content) with { Name = dm.AgentName };
    }

    protected IReadOnlyList<Message> ConvertToMessages(IReadOnlyList<DeliberationMessage> messages)
    {
        return messages.Select(ConvertToMessage).ToList();
    }
}
