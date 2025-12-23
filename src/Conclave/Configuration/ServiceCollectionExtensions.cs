using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Conclave.Abstractions;
using Conclave.Providers;
using Conclave.Voting;

namespace Conclave.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddConclave(
        this IServiceCollection services,
        Action<ConclaveOptions>? configure = null)
    {
        var options = new ConclaveOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        if (options.OpenAi != null)
        {
            services.AddOpenAiProvider(options.OpenAi);
        }

        if (options.Anthropic != null)
        {
            services.AddAnthropicProvider(options.Anthropic);
        }

        if (options.Gemini != null)
        {
            services.AddGeminiProvider(options.Gemini);
        }

        services.TryAddSingleton<IVotingStrategy, MajorityVotingStrategy>();

        return services;
    }

    public static IServiceCollection AddOpenAiProvider(
        this IServiceCollection services,
        OpenAiOptions options)
    {
        services.AddHttpClient<OpenAiProvider>();
        services.AddSingleton(options);
        services.AddSingleton<ILlmProvider>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(OpenAiProvider));
            return new OpenAiProvider(httpClient, options);
        });

        return services;
    }

    public static IServiceCollection AddOpenAiProvider(
        this IServiceCollection services,
        string apiKey,
        string? model = null)
    {
        return services.AddOpenAiProvider(new OpenAiOptions
        {
            ApiKey = apiKey,
            DefaultModel = model ?? "gpt-4o"
        });
    }

    public static IServiceCollection AddAnthropicProvider(
        this IServiceCollection services,
        AnthropicOptions options)
    {
        services.AddHttpClient<AnthropicProvider>();
        services.AddSingleton(options);
        services.AddSingleton<ILlmProvider>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(AnthropicProvider));
            return new AnthropicProvider(httpClient, options);
        });

        return services;
    }

    public static IServiceCollection AddAnthropicProvider(
        this IServiceCollection services,
        string apiKey,
        string? model = null)
    {
        return services.AddAnthropicProvider(new AnthropicOptions
        {
            ApiKey = apiKey,
            DefaultModel = model ?? "claude-sonnet-4-20250514"
        });
    }

    public static IServiceCollection AddGeminiProvider(
        this IServiceCollection services,
        GeminiOptions options)
    {
        services.AddHttpClient<GeminiProvider>();
        services.AddSingleton(options);
        services.AddSingleton<ILlmProvider>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(GeminiProvider));
            return new GeminiProvider(httpClient, options);
        });

        return services;
    }

    public static IServiceCollection AddGeminiProvider(
        this IServiceCollection services,
        string apiKey,
        string? model = null)
    {
        return services.AddGeminiProvider(new GeminiOptions
        {
            ApiKey = apiKey,
            DefaultModel = model ?? "gemini-2.0-flash"
        });
    }

    public static IServiceCollection AddVotingStrategy<TStrategy>(this IServiceCollection services)
        where TStrategy : class, IVotingStrategy
    {
        services.AddSingleton<IVotingStrategy, TStrategy>();
        return services;
    }
}
