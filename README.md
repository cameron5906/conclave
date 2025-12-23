# Quorum

A democratic multi-agent LLM workflow framework for building consensus-driven AI processes in C#.

Quorum makes it easy to orchestrate multiple AI agents with different personalities, have them collaborate on tasks, and reach decisions through various voting and consensus mechanisms.

## Features

- **Multi-Provider Support**: Built-in integrations for OpenAI (GPT-4), Anthropic (Claude), and Google (Gemini)
- **Agent Personalities**: Pre-built personalities (Analyst, Creative, Critic, Diplomat, Expert) or create custom ones
- **Voting Strategies**: Majority, Weighted, Ranked Choice, Consensus, Aggregation, and Expert Panel
- **Deliberation Workflows**: Multi-round agent conversations with budget-controlled termination
- **Tool Support**: Equip agents with custom tools for enhanced capabilities
- **Fluent API**: Builder patterns for easy configuration
- **Dependency Injection**: First-class support for Microsoft.Extensions.DependencyInjection
- **Structured Output**: Support for typed responses from agents
- **Progress Tracking**: Real-time workflow progress updates

## Installation

```bash
# Core library
dotnet add package Quorum

# ASP.NET Core integration (optional)
dotnet add package Quorum.Extensions.AspNetCore
```

## Quick Start

### Basic Usage

```csharp
using Quorum;
using Quorum.Abstractions;
using Quorum.Models;

// Create a session
using var session = new QuorumSession()
    .AddOpenAi("your-openai-api-key")
    .AddAnthropic("your-anthropic-api-key")
    .AddGemini("your-gemini-api-key");

// Add agents with different providers and personalities
session
    .AddAgent("Analyst", session.Providers[0], AgentPersonality.Analyst)
    .AddAgent("Creative", session.Providers[1], AgentPersonality.Creative)
    .AddAgent("Critic", session.Providers[2], AgentPersonality.Critic);

// Execute a task with majority voting
var result = await session.ExecuteAsync(
    "What are the best practices for building microservices?",
    VotingStrategy.Majority);

Console.WriteLine($"Result: {result.Value}");
Console.WriteLine($"Consensus Score: {result.VotingResult?.ConsensusScore:P0}");
```

### Using the Builder Pattern

```csharp
using Quorum.Agents;
using Quorum.Providers;
using Quorum.Workflows;

// Create providers
var openAi = new OpenAiProvider(new HttpClient(), new OpenAiOptions
{
    ApiKey = "your-api-key",
    DefaultModel = "gpt-4o"
});

var claude = new AnthropicProvider(new HttpClient(), new AnthropicOptions
{
    ApiKey = "your-api-key",
    DefaultModel = "claude-sonnet-4-20250514"
});

// Create agents with the builder
var analyst = new AgentBuilder()
    .WithName("Data Analyst")
    .WithProvider(openAi)
    .AsAnalyst()
    .Build();

var creative = new AgentBuilder()
    .WithName("Creative Thinker")
    .WithProvider(claude)
    .AsCreative()
    .Build();

var critic = new AgentBuilder()
    .WithName("Critical Reviewer")
    .WithProvider(openAi)
    .AsCritic()
    .Build();

// Create and execute a workflow
var workflow = Workflow.Create()
    .WithName("Product Ideas")
    .AddAgents(new[] { analyst, creative, critic })
    .WithConsensusVoting()
    .WithArbiter(claude)
    .Build();

var result = await workflow.ExecuteAsync(
    "Generate innovative product ideas for sustainable home goods");
```

### Custom Personalities

```csharp
var agent = new AgentBuilder()
    .WithName("Domain Expert")
    .WithProvider(provider)
    .WithCustomPersonality(p => p
        .WithName("Security Expert")
        .WithDescription("Cybersecurity specialist with defensive mindset")
        .WithSystemPrompt("You are a cybersecurity expert. Focus on security implications, vulnerabilities, and best practices.")
        .WithExpertise("Cybersecurity")
        .WithCreativity(0.4)
        .WithPrecision(0.95)
        .WithStyle(CommunicationStyle.Technical))
    .Build();
```

### Adding Tools to Agents

```csharp
using Quorum.Tools;

var searchTool = new ToolBuilder()
    .WithName("search")
    .WithDescription("Search for information")
    .WithStringParameter("query", "The search query", required: true)
    .WithNumberParameter("limit", "Maximum results to return")
    .WithHandler(async (args) =>
    {
        var parsed = JsonSerializer.Deserialize<SearchArgs>(args);
        var results = await SearchService.SearchAsync(parsed.Query, parsed.Limit);
        return ToolResult.Ok(JsonSerializer.Serialize(results));
    })
    .Build();

var agent = new AgentBuilder()
    .WithName("Research Agent")
    .WithProvider(provider)
    .WithTool(searchTool)
    .Build();
```

### Structured Output

```csharp
public class ProductAnalysis
{
    public string ProductName { get; set; }
    public List<string> Strengths { get; set; }
    public List<string> Weaknesses { get; set; }
    public double MarketPotential { get; set; }
}

var workflow = Workflow.Create<ProductAnalysis>()
    .WithName("Product Analysis")
    .AddAgents(agents)
    .WithAggregation()
    .Build();

var result = await workflow.ExecuteAsync("Analyze the market potential of electric bicycles");
var analysis = result.Value; // Typed as ProductAnalysis
```

### ASP.NET Core Dependency Injection

Install the ASP.NET Core extension package:

```bash
dotnet add package Quorum.Extensions.AspNetCore
```

#### Configuration via appsettings.json

```json
{
  "Quorum": {
    "OpenAi": {
      "ApiKey": "your-openai-api-key",
      "DefaultModel": "gpt-4o"
    },
    "Anthropic": {
      "ApiKey": "your-anthropic-api-key",
      "DefaultModel": "claude-sonnet-4-20250514"
    },
    "Gemini": {
      "ApiKey": "your-gemini-api-key",
      "DefaultModel": "gemini-2.0-flash"
    },
    "Agents": [
      {
        "Id": "analyst",
        "Name": "Data Analyst",
        "Provider": "openai",
        "Personality": { "Preset": "analyst" }
      },
      {
        "Id": "creative",
        "Name": "Creative Thinker",
        "Provider": "anthropic",
        "Personality": { "Preset": "creative" }
      },
      {
        "Id": "critic",
        "Name": "Critical Reviewer",
        "Provider": "gemini",
        "Personality": { "Preset": "critic" }
      }
    ]
  }
}
```

#### Program.cs Setup

```csharp
using Quorum.Extensions.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add Quorum with configuration from appsettings.json
builder.Services.AddQuorum(builder.Configuration);

var app = builder.Build();
```

#### Using IQuorumFactory in Controllers

```csharp
[ApiController]
[Route("api/[controller]")]
public class AnalysisController : ControllerBase
{
    private readonly IQuorumFactory _quorum;

    public AnalysisController(IQuorumFactory quorum)
    {
        _quorum = quorum;
    }

    [HttpPost]
    public async Task<IActionResult> Analyze([FromBody] AnalysisRequest request)
    {
        var result = await _quorum.ExecuteAsync(
            request.Task,
            VotingStrategy.Consensus);

        if (!result.IsSuccess)
        {
            return BadRequest(result.Error);
        }

        return Ok(new
        {
            result.Value,
            result.VotingResult?.ConsensusScore
        });
    }
}
```

#### Programmatic Configuration

```csharp
// Configure with code instead of appsettings.json
builder.Services.AddQuorum(config =>
{
    config.OpenAi = new OpenAiConfiguration
    {
        ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!,
        DefaultModel = "gpt-4o"
    };
    config.Anthropic = new AnthropicConfiguration
    {
        ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")!
    };
});

// Add agents programmatically
builder.Services.AddAgent("Analyst", "openai", AgentPersonality.Analyst);
builder.Services.AddAgent("Creative", "anthropic", AgentPersonality.Creative);

// Or with full customization
builder.Services.AddAgent((builder, sp) =>
{
    var registry = sp.GetRequiredService<ProviderRegistry>();
    builder
        .WithName("Custom Agent")
        .WithProvider(registry.Get("openai")!)
        .WithCustomPersonality(p => p
            .WithName("Domain Expert")
            .WithExpertise("Machine Learning")
            .WithPrecision(0.95));
});
```

#### Adding Providers Individually

```csharp
builder.Services.AddOpenAiProvider("your-api-key", "gpt-4o");
builder.Services.AddAnthropicProvider("your-api-key");
builder.Services.AddGeminiProvider("your-api-key");
```

## Deliberation Workflows

Deliberation enables multi-round conversations between agents, allowing them to refine their positions, debate, and reach consensus through iterative discussion.

### Basic Deliberation

```csharp
using Quorum.Deliberation;

var deliberation = Deliberation.Create()
    .WithName("Strategic Planning")
    .AddAgents(new[] { analyst, creative, critic })
    .WithRoundRobin()
    .WithMaxRounds(5)
    .WithConvergenceThreshold(0.85)
    .WithConsensusVoting()
    .Build();

var result = await deliberation.ExecuteAsync(
    "What should be our product strategy for 2025?");

Console.WriteLine($"Final Answer: {result.Value}");
Console.WriteLine($"Rounds: {result.TotalRounds}");
Console.WriteLine($"Convergence: {result.FinalConvergenceScore:P0}");
```

### Deliberation Modes

| Mode | Description |
|------|-------------|
| **RoundRobin** | Each agent speaks in turn, seeing all prior messages |
| **Debate** | Agents directly respond to and challenge each other |
| **Moderated** | A moderator agent guides the discussion |
| **FreeForm** | Agents contribute in parallel each round |

```csharp
// Debate mode - agents challenge each other
var debate = Deliberation.Create()
    .AddAgents(agents)
    .WithDebate()
    .WithMaxRounds(3)
    .Build();

// Moderated mode - a moderator guides discussion
var moderated = Deliberation.Create()
    .AddAgents(agents)
    .WithModeration(moderatorAgent)
    .WithMaxRounds(5)
    .Build();
```

### Budget-Controlled Termination

Control when deliberation ends using multiple budget strategies:

```csharp
var deliberation = Deliberation.Create()
    .AddAgents(agents)
    .WithBudget(budget => budget
        .WithMaxRounds(10)                    // Stop after 10 rounds
        .WithMaxTokens(50000)                 // Stop at 50k tokens
        .WithMaxTime(TimeSpan.FromMinutes(5)) // Stop after 5 minutes
        .WithConvergenceThreshold(0.9))       // Stop when converged
    .Build();
```

### Agent-Based Termination

Let an LLM agent decide when the discussion should end:

```csharp
var judge = new AgentBuilder()
    .WithName("Discussion Judge")
    .WithProvider(provider)
    .WithCustomPersonality(p => p
        .WithSystemPrompt("You evaluate discussions and decide if consensus is reached."))
    .Build();

var deliberation = Deliberation.Create()
    .AddAgents(agents)
    .WithAgentTerminator(judge, confidenceThreshold: 0.8)
    .Build();
```

### Workflow-Based Termination

Use a full multi-agent workflow to decide termination:

```csharp
var terminatorWorkflow = Workflow.Create<TerminatorResponse>()
    .AddAgents(evaluatorAgents)
    .WithConsensusVoting()
    .Build();

var deliberation = Deliberation.Create()
    .AddAgents(agents)
    .WithWorkflowTerminator(terminatorWorkflow)
    .Build();
```

### Custom Termination Conditions

```csharp
var deliberation = Deliberation.Create()
    .AddAgents(agents)
    .TerminateWhen(
        state => state.Transcript.Count >= 20,
        "Maximum messages reached")
    .TerminateWhenAsync(async state =>
    {
        // Custom async logic
        return await CheckExternalCondition(state);
    })
    .Build();
```

### Deliberation Progress Tracking

```csharp
var options = new DeliberationOptions
{
    OnProgress = progress =>
    {
        Console.WriteLine($"Round {progress.CurrentRound}/{progress.MaxRounds}");
        Console.WriteLine($"Speaker: {progress.CurrentSpeaker}");
        Console.WriteLine($"Tokens: {progress.TokensUsed}/{progress.TokenBudget}");
        Console.WriteLine($"Convergence: {progress.ConvergenceScore:P0}");
    }
};

var result = await deliberation.ExecuteAsync(task, options);
```

### Structured Output with Deliberation

```csharp
public class StrategicPlan
{
    public string Vision { get; set; }
    public List<string> Goals { get; set; }
    public List<string> Risks { get; set; }
}

var deliberation = Deliberation.Create<StrategicPlan>()
    .AddAgents(agents)
    .WithMaxRounds(5)
    .WithAggregation()
    .Build();

var result = await deliberation.ExecuteAsync("Create a 5-year strategic plan");
var plan = result.Value; // Typed as StrategicPlan
```

## Voting Strategies

| Strategy | Description | Best For |
|----------|-------------|----------|
| **Majority** | Simple majority wins | Quick decisions, binary choices |
| **Weighted** | Agents have different voting weights | Expert opinions, trust-based systems |
| **RankedChoice** | Eliminates lowest until majority | Multiple options, preference-based |
| **Consensus** | Synthesizes all responses | Complex problems, nuanced answers |
| **Aggregation** | Combines all responses | Comprehensive outputs, research |
| **ExpertPanel** | LLM evaluates each response | Quality assessment, benchmarking |

## Agent Personalities

### Built-in Personalities

- **Analyst**: Methodical, data-driven, high precision
- **Creative**: Innovative, unconventional, high creativity
- **Critic**: Thorough reviewer, devil's advocate
- **Diplomat**: Consensus builder, mediator
- **Expert(domain)**: Domain specialist with deep knowledge

### Personality Properties

```csharp
public class AgentPersonality
{
    public string Name { get; init; }
    public string Description { get; init; }
    public string SystemPrompt { get; init; }
    public double Creativity { get; init; }      // 0.0 to 1.0
    public double Precision { get; init; }       // 0.0 to 1.0
    public string? Expertise { get; init; }
    public CommunicationStyle CommunicationStyle { get; init; }
}
```

## Workflow Options

```csharp
var options = new WorkflowOptions
{
    MaxRetries = 3,
    Timeout = TimeSpan.FromMinutes(5),
    EnableParallelExecution = true,
    RequireConsensus = true,
    MinimumConsensusScore = 0.7,
    OnProgress = progress =>
    {
        Console.WriteLine($"Stage: {progress.Stage}, Progress: {progress.ProgressPercentage:F0}%");
    }
};

var result = await workflow.ExecuteAsync(task, options);
```

## Supported LLM Providers

### OpenAI
```csharp
var provider = new OpenAiProvider(httpClient, new OpenAiOptions
{
    ApiKey = "your-api-key",
    DefaultModel = "gpt-4o",           // Default model
    DefaultTemperature = 0.7,           // Optional
    DefaultMaxTokens = 4096,            // Optional
    Organization = "org-id",            // Optional
    BaseUrl = "https://custom-url"      // Optional, for Azure OpenAI
});
```

### Anthropic Claude
```csharp
var provider = new AnthropicProvider(httpClient, new AnthropicOptions
{
    ApiKey = "your-api-key",
    DefaultModel = "claude-sonnet-4-20250514",
    DefaultMaxTokens = 4096,
    ApiVersion = "2023-06-01"           // Optional
});
```

### Google Gemini
```csharp
var provider = new GeminiProvider(httpClient, new GeminiOptions
{
    ApiKey = "your-api-key",
    DefaultModel = "gemini-2.0-flash",
    DefaultTemperature = 0.7
});
```

## Packages

| Package | Description |
|---------|-------------|
| `Quorum` | Core library with all agents, providers, and voting strategies |
| `Quorum.Extensions.AspNetCore` | ASP.NET Core DI integration with `IQuorumFactory` |

## Architecture

```
Quorum/
├── src/
│   ├── Quorum/                          # Core library
│   │   ├── Abstractions/                # Core interfaces
│   │   ├── Agents/                      # Agent implementation
│   │   ├── Providers/                   # LLM providers (OpenAI, Claude, Gemini)
│   │   ├── Voting/                      # Voting strategies
│   │   ├── Workflows/                   # Workflow orchestration
│   │   ├── Deliberation/                # Multi-round deliberation workflows
│   │   ├── Tools/                       # Tool definitions
│   │   ├── Models/                      # Data models
│   │   ├── Configuration/               # Basic DI support
│   │   └── QuorumSession.cs             # High-level session API
│   │
│   └── Quorum.Extensions.AspNetCore/    # ASP.NET Core extensions
│       ├── QuorumConfiguration.cs       # Configuration models
│       ├── QuorumServiceCollectionExtensions.cs
│       ├── IQuorumFactory.cs
│       ├── QuorumFactory.cs
│       └── ProviderRegistry.cs
│
├── samples/
│   └── Quorum.Samples.CodeReview/       # Multi-agent code review sample
│
└── tests/
    └── Quorum.Tests/                    # Unit tests
```

## Requirements

- .NET 9.0 or later
- API keys for your chosen LLM providers

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License.
