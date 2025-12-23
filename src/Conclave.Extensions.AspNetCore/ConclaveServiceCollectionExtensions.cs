using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Conclave.Abstractions;
using Conclave.Agents;
using Conclave.Providers;
using Conclave.Voting;

namespace Conclave.Extensions.AspNetCore;

public static class ConclaveServiceCollectionExtensions
{
    public static IServiceCollection AddConclave(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        return services.AddConclave(configuration, ConclaveConfiguration.SectionName);
    }

    public static IServiceCollection AddConclave(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
    {
        var section = configuration.GetSection(sectionName);
        services.Configure<ConclaveConfiguration>(section);

        var config = section.Get<ConclaveConfiguration>() ?? new ConclaveConfiguration();

        return services.AddConclaveCore(config);
    }

    public static IServiceCollection AddConclave(
        this IServiceCollection services,
        Action<ConclaveConfiguration> configure)
    {
        var config = new ConclaveConfiguration();
        configure(config);

        services.Configure(configure);
        return services.AddConclaveCore(config);
    }

    private static IServiceCollection AddConclaveCore(
        this IServiceCollection services,
        ConclaveConfiguration config)
    {
        services.AddHttpClient();

        var providerRegistry = new ProviderRegistry();
        services.AddSingleton(providerRegistry);

        if (config.OpenAi != null && !string.IsNullOrEmpty(config.OpenAi.ApiKey))
        {
            services.AddOpenAiProvider(config.OpenAi);
        }

        if (config.Anthropic != null && !string.IsNullOrEmpty(config.Anthropic.ApiKey))
        {
            services.AddAnthropicProvider(config.Anthropic);
        }

        if (config.Gemini != null && !string.IsNullOrEmpty(config.Gemini.ApiKey))
        {
            services.AddGeminiProvider(config.Gemini);
        }

        services.TryAddSingleton<IVotingStrategy, MajorityVotingStrategy>();
        services.AddSingleton<IConclaveFactory, ConclaveFactory>();

        if (config.Agents.Any())
        {
            services.AddAgentsFromConfiguration(config.Agents);
        }

        return services;
    }

    public static IServiceCollection AddOpenAiProvider(
        this IServiceCollection services,
        OpenAiConfiguration config)
    {
        services.AddHttpClient<OpenAiProvider>();

        var options = new OpenAiOptions
        {
            ApiKey = config.ApiKey,
            Organization = config.Organization,
            BaseUrl = config.BaseUrl,
            DefaultModel = config.DefaultModel,
            DefaultTemperature = config.DefaultTemperature,
            DefaultMaxTokens = config.DefaultMaxTokens
        };

        services.AddSingleton(options);
        services.AddSingleton<ILlmProvider>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(OpenAiProvider));
            var provider = new OpenAiProvider(httpClient, options);
            sp.GetRequiredService<ProviderRegistry>().Register("openai", provider);
            return provider;
        });

        return services;
    }

    public static IServiceCollection AddOpenAiProvider(
        this IServiceCollection services,
        string apiKey,
        string? model = null)
    {
        return services.AddOpenAiProvider(new OpenAiConfiguration
        {
            ApiKey = apiKey,
            DefaultModel = model ?? "gpt-4o"
        });
    }

    public static IServiceCollection AddAnthropicProvider(
        this IServiceCollection services,
        AnthropicConfiguration config)
    {
        services.AddHttpClient<AnthropicProvider>();

        var options = new AnthropicOptions
        {
            ApiKey = config.ApiKey,
            BaseUrl = config.BaseUrl,
            ApiVersion = config.ApiVersion,
            DefaultModel = config.DefaultModel,
            DefaultTemperature = config.DefaultTemperature,
            DefaultMaxTokens = config.DefaultMaxTokens
        };

        services.AddSingleton(options);
        services.AddSingleton<ILlmProvider>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(AnthropicProvider));
            var provider = new AnthropicProvider(httpClient, options);
            sp.GetRequiredService<ProviderRegistry>().Register("anthropic", provider);
            return provider;
        });

        return services;
    }

    public static IServiceCollection AddAnthropicProvider(
        this IServiceCollection services,
        string apiKey,
        string? model = null)
    {
        return services.AddAnthropicProvider(new AnthropicConfiguration
        {
            ApiKey = apiKey,
            DefaultModel = model ?? "claude-sonnet-4-20250514"
        });
    }

    public static IServiceCollection AddGeminiProvider(
        this IServiceCollection services,
        GeminiConfiguration config)
    {
        services.AddHttpClient<GeminiProvider>();

        var options = new GeminiOptions
        {
            ApiKey = config.ApiKey,
            BaseUrl = config.BaseUrl,
            DefaultModel = config.DefaultModel,
            DefaultTemperature = config.DefaultTemperature,
            DefaultMaxTokens = config.DefaultMaxTokens
        };

        services.AddSingleton(options);
        services.AddSingleton<ILlmProvider>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(GeminiProvider));
            var provider = new GeminiProvider(httpClient, options);
            sp.GetRequiredService<ProviderRegistry>().Register("gemini", provider);
            return provider;
        });

        return services;
    }

    public static IServiceCollection AddGeminiProvider(
        this IServiceCollection services,
        string apiKey,
        string? model = null)
    {
        return services.AddGeminiProvider(new GeminiConfiguration
        {
            ApiKey = apiKey,
            DefaultModel = model ?? "gemini-2.0-flash"
        });
    }

    private static IServiceCollection AddAgentsFromConfiguration(
        this IServiceCollection services,
        List<AgentConfiguration> agents)
    {
        foreach (var agentConfig in agents)
        {
            services.AddSingleton<IAgent>(sp =>
            {
                var registry = sp.GetRequiredService<ProviderRegistry>();
                var provider = registry.Get(agentConfig.Provider)
                    ?? throw new InvalidOperationException($"Provider '{agentConfig.Provider}' not found for agent '{agentConfig.Name}'");

                var builder = new AgentBuilder()
                    .WithId(agentConfig.Id)
                    .WithName(agentConfig.Name)
                    .WithProvider(provider);

                if (!string.IsNullOrEmpty(agentConfig.Model))
                {
                    builder.WithModel(agentConfig.Model);
                }

                if (agentConfig.Personality != null)
                {
                    builder.WithPersonality(BuildPersonality(agentConfig.Personality));
                }

                return builder.Build();
            });
        }

        return services;
    }

    private static AgentPersonality BuildPersonality(PersonalityConfiguration config)
    {
        if (!string.IsNullOrEmpty(config.Preset))
        {
            return config.Preset.ToLowerInvariant() switch
            {
                "analyst" => AgentPersonality.Analyst,
                "creative" => AgentPersonality.Creative,
                "critic" => AgentPersonality.Critic,
                "diplomat" => AgentPersonality.Diplomat,
                _ when config.Preset.StartsWith("expert:", StringComparison.OrdinalIgnoreCase)
                    => AgentPersonality.Expert(config.Preset[7..]),
                _ => AgentPersonality.Default
            };
        }

        var style = CommunicationStyle.Professional;
        if (!string.IsNullOrEmpty(config.CommunicationStyle))
        {
            Enum.TryParse<CommunicationStyle>(config.CommunicationStyle, true, out style);
        }

        return new AgentPersonality
        {
            Name = config.Name ?? "Custom",
            Description = config.Description ?? string.Empty,
            SystemPrompt = config.SystemPrompt ?? string.Empty,
            Expertise = config.Expertise,
            Creativity = config.Creativity ?? 0.7,
            Precision = config.Precision ?? 0.8,
            CommunicationStyle = style,
            Traits = config.Traits ?? new Dictionary<string, string>()
        };
    }

    public static IServiceCollection AddVotingStrategy<TStrategy>(this IServiceCollection services)
        where TStrategy : class, IVotingStrategy
    {
        services.RemoveAll<IVotingStrategy>();
        services.AddSingleton<IVotingStrategy, TStrategy>();
        return services;
    }

    public static IServiceCollection AddAgent(
        this IServiceCollection services,
        string name,
        string providerKey,
        AgentPersonality? personality = null)
    {
        services.AddSingleton<IAgent>(sp =>
        {
            var registry = sp.GetRequiredService<ProviderRegistry>();
            var provider = registry.Get(providerKey)
                ?? throw new InvalidOperationException($"Provider '{providerKey}' not registered");

            return new AgentBuilder()
                .WithName(name)
                .WithProvider(provider)
                .WithPersonality(personality ?? AgentPersonality.Default)
                .Build();
        });

        return services;
    }

    public static IServiceCollection AddAgent(
        this IServiceCollection services,
        Action<AgentBuilder, IServiceProvider> configure)
    {
        services.AddSingleton<IAgent>(sp =>
        {
            var builder = new AgentBuilder();
            configure(builder, sp);
            return builder.Build();
        });

        return services;
    }
}
