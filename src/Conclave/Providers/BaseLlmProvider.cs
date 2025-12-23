using System.Runtime.CompilerServices;
using System.Text.Json;
using Conclave.Abstractions;
using Conclave.Models;
using Conclave.Tools;
using Microsoft.Extensions.Logging;

namespace Conclave.Providers;

public abstract class BaseLlmProvider : ILlmProvider
{
    protected readonly HttpClient HttpClient;
    protected readonly ILogger? Logger;
    protected readonly JsonSerializerOptions JsonOptions;

    public abstract string ProviderId { get; }
    public abstract string DisplayName { get; }

    protected BaseLlmProvider(HttpClient httpClient, ILogger? logger = null)
    {
        HttpClient = httpClient;
        Logger = logger;
        JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false
        };
    }

    public abstract Task<LlmResponse> CompleteAsync(
        IReadOnlyList<Message> messages,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default);

    public abstract Task<LlmResponse> CompleteWithToolsAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition> tools,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default);

    public abstract IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<Message> messages,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default);

    protected async Task<T> SendRequestAsync<T>(
        string endpoint,
        object payload,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        Logger?.LogDebug("Sending request to {Endpoint}", endpoint);

        var response = await HttpClient.PostAsync(endpoint, content, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            Logger?.LogError("Request failed: {StatusCode} - {Content}", response.StatusCode, responseContent);
            throw new LlmProviderException($"Request failed: {response.StatusCode}", responseContent);
        }

        return JsonSerializer.Deserialize<T>(responseContent, JsonOptions)
            ?? throw new LlmProviderException("Failed to deserialize response", responseContent);
    }

    protected async IAsyncEnumerable<string> StreamRequestAsync(
        string endpoint,
        object payload,
        Func<string, string?> extractContent,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith("data: "))
            {
                var data = line[6..];
                if (data == "[DONE]") break;

                var content = extractContent(data);
                if (content != null)
                {
                    yield return content;
                }
            }
        }
    }
}

public class LlmProviderException : Exception
{
    public string? ResponseContent { get; }

    public LlmProviderException(string message, string? responseContent = null) : base(message)
    {
        ResponseContent = responseContent;
    }
}
