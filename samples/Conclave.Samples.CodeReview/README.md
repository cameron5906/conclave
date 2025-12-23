# Multi-Agent Code Review Sample

This sample demonstrates Quorum's multi-agent capabilities with structured outputs by implementing a realistic code review system where multiple AI agents with different expertise analyze code from different perspectives.

## What This Sample Shows

- **Structured Outputs**: Agents return typed `CodeReviewResult` objects instead of free-form text
- **Specialized Agents**: Four distinct expert personas (Security, Performance, Architecture, Pragmatic)
- **Custom Personalities**: Each agent has tailored system prompts, creativity/precision settings
- **Aggregation Strategy**: Combines all expert opinions into a unified report
- **Progress Tracking**: Real-time feedback during the review process
- **Parallel Execution**: All agents analyze code simultaneously

## The Review Panel

| Agent | Focus Area | Personality |
|-------|------------|-------------|
| Security Analyst | SQL injection, auth, encryption, OWASP | High precision, low creativity |
| Performance Engineer | Complexity, memory, caching, async | Technical, detail-oriented |
| Software Architect | SOLID, patterns, maintainability | Balanced, big-picture |
| Pragmatic Developer | Readability, edge cases, delivery | Direct, practical |

## Sample Code Under Review

The sample reviews a deliberately flawed `UserService.cs` that contains:
- SQL injection vulnerabilities
- Plain-text password storage
- Password exposure in API responses
- Static cache without expiration
- Missing input validation
- Synchronous operations in async context

This makes it an excellent demonstration of how different experts catch different issues.

## Running the Sample

1. Set your OpenAI API key:
   ```bash
   # Windows
   set OPENAI_API_KEY=your-key-here

   # Linux/Mac
   export OPENAI_API_KEY=your-key-here
   ```

2. Run the sample:
   ```bash
   cd samples/Quorum.Samples.CodeReview
   dotnet run
   ```

## Expected Output

```
╔══════════════════════════════════════════════════════════════╗
║       Quorum Multi-Agent Code Review System                  ║
║       Powered by Democratic AI Decision Making               ║
╚══════════════════════════════════════════════════════════════╝

Creating review panel with specialized agents...

Code under review:
────────────────────────────────────────────────────────────────
UserService.cs - Authentication and user management service
────────────────────────────────════════════════────────────────

Starting multi-agent code review...

🔄 Starting workflow execution
🔍 Agent Security Analyst processing
🔍 Agent Performance Engineer processing
🔍 Agent Software Architect processing
🔍 Agent Pragmatic Developer processing
...

════════════════════════════════════════════════════════════════
                    REVIEW RESULTS
════════════════════════════════════════════════════════════════

Individual Agent Assessments:
────────────────────────────────────────────────────────────────

📋 Security Analyst
   Response time: 3.2s
   Approval: ❌ Not Recommended
   Confidence: 95%
   Findings: 5 issues found

📋 Performance Engineer
   Response time: 2.8s
   ...

════════════════════════════════════════════════════════════════
              AGGREGATED FINAL VERDICT
════════════════════════════════════════════════════════════════

📝 Summary: Critical security vulnerabilities require immediate attention...

Overall Scores (1-10):
   Code Quality:    ████░░░░░░ 4/10
   Security:        ██░░░░░░░░ 2/10
   Performance:     █████░░░░░ 5/10
   Maintainability: ████░░░░░░ 4/10
   Test Coverage:   █░░░░░░░░░ 1/10

🔍 Key Findings:

   🔴 [Critical] Security
      SQL Injection vulnerability in authentication
      📍 AuthenticateAsync method, line 12
      💡 Use parameterized queries

   🔴 [Critical] Security
      Plain-text password storage and comparison
      📍 AuthenticateAsync and UpdateUserPassword
      💡 Implement password hashing with bcrypt or Argon2
...

────────────────────────────────────────────────────────────────
Final Verdict: ❌ CHANGES REQUESTED (Confidence: 92%)
────────────────────────────────────────────────────────────────

Consensus Score: 100%
Strategy Used: Aggregation

Total execution time: 8.4s
```

## Using Different Providers

To use Anthropic Claude instead:

```csharp
var provider = new AnthropicProvider(httpClient, new AnthropicOptions
{
    ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")!,
    DefaultModel = "claude-sonnet-4-20250514"
});
```

To use Google Gemini:

```csharp
var provider = new GeminiProvider(httpClient, new GeminiOptions
{
    ApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")!,
    DefaultModel = "gemini-2.0-flash"
});
```

## Customizing the Review

### Add More Experts

```csharp
var accessibilityExpert = new AgentBuilder()
    .WithName("Accessibility Expert")
    .WithProvider(provider)
    .AsExpert("Web Accessibility")
    .Build();
```

### Change Voting Strategy

```csharp
// Use consensus to synthesize all opinions
.WithConsensusVoting()

// Use majority for binary approve/reject
.WithMajorityVoting()

// Use expert panel to have an LLM judge responses
.WithExpertPanel()
```

### Weight Expert Opinions

```csharp
.WithWeightedVoting()
.WithAgentWeight("security", 2.0)  // Security gets 2x vote weight
.WithAgentWeight("performance", 1.5)
```
