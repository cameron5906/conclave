using System.Diagnostics;
using System.Text.Json;
using Conclave.Abstractions;
using Conclave.Models;
using Conclave.Tools;
using Microsoft.Extensions.Logging;

namespace Conclave.Agents;

public class ConclaveAgent : IAgent
{
    private readonly ILogger? _logger;
    private readonly LlmCompletionOptions _defaultOptions;
    private readonly List<ToolDefinition> _tools = new();

    public string Id { get; }
    public string Name { get; }
    public AgentPersonality Personality { get; }
    public ILlmProvider Provider { get; }
    public IReadOnlyList<ToolDefinition> AvailableTools => _tools.AsReadOnly();

    public ConclaveAgent(
        string id,
        string name,
        AgentPersonality personality,
        ILlmProvider provider,
        IEnumerable<ToolDefinition>? tools = null,
        LlmCompletionOptions? defaultOptions = null,
        ILogger? logger = null)
    {
        Id = id;
        Name = name;
        Personality = personality;
        Provider = provider;
        _logger = logger;
        _defaultOptions = defaultOptions ?? new LlmCompletionOptions
        {
            Temperature = personality.Creativity,
            SystemPrompt = BuildSystemPrompt(personality)
        };

        if (tools != null)
        {
            _tools.AddRange(tools);
        }
    }

    public async Task<AgentResponse> ProcessAsync(
        string task,
        IReadOnlyList<Message>? context = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var messages = BuildMessages(task, context);

        try
        {
            LlmResponse response;
            if (_tools.Any())
            {
                response = await ProcessWithToolsAsync(messages, cancellationToken);
            }
            else
            {
                response = await Provider.CompleteAsync(messages, _defaultOptions, cancellationToken);
            }

            stopwatch.Stop();
            return new AgentResponse
            {
                AgentId = Id,
                AgentName = Name,
                Response = response.Content,
                ResponseTime = stopwatch.Elapsed,
                Usage = response.Usage
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Agent {AgentId} failed to process task", Id);
            stopwatch.Stop();
            return new AgentResponse
            {
                AgentId = Id,
                AgentName = Name,
                Response = $"Error: {ex.Message}",
                ResponseTime = stopwatch.Elapsed
            };
        }
    }

    public async Task<AgentResponse> ProcessWithStructuredOutputAsync<T>(
        string task,
        IReadOnlyList<Message>? context = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        var schemaPrompt = GenerateSchemaPrompt<T>();
        var augmentedTask = $"{task}\n\n{schemaPrompt}";
        var messages = BuildMessages(augmentedTask, context);

        try
        {
            var response = await Provider.CompleteAsync(messages, _defaultOptions, cancellationToken);
            var structuredOutput = TryParseStructuredOutput<T>(response.Content);

            stopwatch.Stop();
            return new AgentResponse
            {
                AgentId = Id,
                AgentName = Name,
                Response = response.Content,
                StructuredOutput = structuredOutput,
                ResponseTime = stopwatch.Elapsed,
                Usage = response.Usage
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Agent {AgentId} failed structured output processing", Id);
            stopwatch.Stop();
            return new AgentResponse
            {
                AgentId = Id,
                AgentName = Name,
                Response = $"Error: {ex.Message}",
                ResponseTime = stopwatch.Elapsed
            };
        }
    }

    public async Task<AgentResponse> VoteAsync(
        string task,
        IReadOnlyList<AgentResponse> otherResponses,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var votingPrompt = BuildVotingPrompt(task, otherResponses);
        var messages = new List<Message> { Message.User(votingPrompt) };

        try
        {
            var response = await Provider.CompleteAsync(messages, _defaultOptions, cancellationToken);
            var vote = ExtractVote(response.Content, otherResponses);

            stopwatch.Stop();
            return new AgentResponse
            {
                AgentId = Id,
                AgentName = Name,
                Response = response.Content,
                StructuredOutput = vote,
                ResponseTime = stopwatch.Elapsed,
                Usage = response.Usage
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Agent {AgentId} failed to vote", Id);
            stopwatch.Stop();
            return new AgentResponse
            {
                AgentId = Id,
                AgentName = Name,
                Response = $"Error: {ex.Message}",
                ResponseTime = stopwatch.Elapsed
            };
        }
    }

    private async Task<LlmResponse> ProcessWithToolsAsync(
        List<Message> messages,
        CancellationToken cancellationToken)
    {
        const int maxIterations = 10;
        var iteration = 0;

        while (iteration < maxIterations)
        {
            var response = await Provider.CompleteWithToolsAsync(messages, _tools, _defaultOptions, cancellationToken);

            if (response.ToolCalls == null || !response.ToolCalls.Any())
            {
                return response;
            }

            messages.Add(Message.Assistant(response.Content) with { ToolCalls = response.ToolCalls });

            foreach (var toolCall in response.ToolCalls)
            {
                var tool = _tools.FirstOrDefault(t => t.Name == toolCall.Name);
                if (tool?.Handler == null)
                {
                    messages.Add(Message.Tool($"Error: Unknown tool '{toolCall.Name}'", toolCall.Id));
                    continue;
                }

                try
                {
                    var result = await tool.Handler(toolCall.Arguments, cancellationToken);
                    messages.Add(Message.Tool(result.Success ? result.Output : $"Error: {result.Error}", toolCall.Id));
                }
                catch (Exception ex)
                {
                    messages.Add(Message.Tool($"Error executing tool: {ex.Message}", toolCall.Id));
                }
            }

            iteration++;
        }

        return new LlmResponse { Content = "Maximum tool iterations reached" };
    }

    private List<Message> BuildMessages(string task, IReadOnlyList<Message>? context)
    {
        var messages = new List<Message>();

        if (context != null)
        {
            messages.AddRange(context);
        }

        messages.Add(Message.User(task));
        return messages;
    }

    private static string BuildSystemPrompt(AgentPersonality personality)
    {
        var prompt = personality.SystemPrompt;

        if (!string.IsNullOrEmpty(personality.Expertise))
        {
            prompt += $"\n\nYou have deep expertise in: {personality.Expertise}";
        }

        if (personality.Traits.Any())
        {
            prompt += "\n\nKey traits:";
            foreach (var trait in personality.Traits)
            {
                prompt += $"\n- {trait.Key}: {trait.Value}";
            }
        }

        prompt += personality.CommunicationStyle switch
        {
            CommunicationStyle.Technical => "\n\nCommunicate in a technical, precise manner.",
            CommunicationStyle.Casual => "\n\nCommunicate in a friendly, approachable manner.",
            CommunicationStyle.Direct => "\n\nBe direct and to the point.",
            CommunicationStyle.Empathetic => "\n\nBe understanding and considerate of different perspectives.",
            CommunicationStyle.Academic => "\n\nUse academic rigor and cite reasoning.",
            _ => "\n\nCommunicate professionally and clearly."
        };

        return prompt;
    }

    private static string BuildVotingPrompt(string task, IReadOnlyList<AgentResponse> responses)
    {
        var prompt = $"Original task: {task}\n\n";
        prompt += "The following responses have been provided by different agents. Please evaluate each and vote for the best one.\n\n";

        for (int i = 0; i < responses.Count; i++)
        {
            prompt += $"Response {i + 1} (from {responses[i].AgentName}):\n{responses[i].Response}\n\n";
        }

        prompt += "Please analyze each response and indicate which one you believe is the best. ";
        prompt += "Explain your reasoning and respond with the number of your chosen response (1, 2, 3, etc.).";

        return prompt;
    }

    private static VoteResult ExtractVote(string response, IReadOnlyList<AgentResponse> candidates)
    {
        for (int i = candidates.Count; i >= 1; i--)
        {
            if (response.Contains(i.ToString()))
            {
                return new VoteResult
                {
                    ChosenAgentId = candidates[i - 1].AgentId,
                    Reasoning = response
                };
            }
        }

        return new VoteResult
        {
            ChosenAgentId = candidates.First().AgentId,
            Reasoning = response
        };
    }

    private static string GenerateSchemaPrompt<T>()
    {
        var type = typeof(T);
        var properties = type.GetProperties();
        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties.ToDictionary(
                p => p.Name,
                p => new { type = GetJsonType(p.PropertyType) }
            )
        };

        return $"Please respond with valid JSON matching this schema:\n```json\n{JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true })}\n```";
    }

    private static string GetJsonType(Type type)
    {
        if (type == typeof(string)) return "string";
        if (type == typeof(int) || type == typeof(long) || type == typeof(double) || type == typeof(float) || type == typeof(decimal)) return "number";
        if (type == typeof(bool)) return "boolean";
        if (type.IsArray || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return "array";
        return "object";
    }

    private static T? TryParseStructuredOutput<T>(string content) where T : class
    {
        try
        {
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = content.Substring(jsonStart, jsonEnd - jsonStart + 1);
                return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }
        catch
        {
        }
        return null;
    }
}

public class VoteResult
{
    public string ChosenAgentId { get; init; } = string.Empty;
    public string Reasoning { get; init; } = string.Empty;
}
