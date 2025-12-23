using System.Text.Json;
using Conclave;
using Conclave.Abstractions;
using Conclave.Agents;
using Conclave.Models;
using Conclave.Providers;
using Conclave.Workflows;
using Conclave.Samples.CodeReview.Models;

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║      Conclave Multi-Agent Code Review System                 ║");
Console.WriteLine("║       Powered by Democratic AI Decision Making               ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine("Please set OPENAI_API_KEY environment variable.");
    Console.WriteLine("You can also modify this sample to use Anthropic or Gemini.");
    return;
}

var httpClient = new HttpClient();
var provider = new OpenAiProvider(httpClient, new OpenAiOptions
{
    ApiKey = apiKey,
    DefaultModel = "gpt-4o"
});

Console.WriteLine("Creating review panel with specialized agents...\n");

var securityExpert = new AgentBuilder()
    .WithId("security")
    .WithName("Security Analyst")
    .WithProvider(provider)
    .WithCustomPersonality(p => p
        .WithName("Security Analyst")
        .WithSystemPrompt(@"You are a senior security engineer specializing in application security.
Focus on: authentication, authorization, input validation, SQL injection, XSS, CSRF,
secrets management, encryption, and secure coding practices.
Be thorough but prioritize critical vulnerabilities.")
        .WithExpertise("Application Security")
        .WithCreativity(0.3)
        .WithPrecision(0.95)
        .WithStyle(CommunicationStyle.Technical))
    .Build();

var performanceExpert = new AgentBuilder()
    .WithId("performance")
    .WithName("Performance Engineer")
    .WithProvider(provider)
    .WithCustomPersonality(p => p
        .WithName("Performance Engineer")
        .WithSystemPrompt(@"You are a performance optimization specialist.
Focus on: algorithmic complexity, memory usage, database query efficiency,
caching opportunities, async patterns, resource leaks, and scalability concerns.
Identify bottlenecks and suggest concrete optimizations.")
        .WithExpertise("Performance Engineering")
        .WithCreativity(0.4)
        .WithPrecision(0.9)
        .WithStyle(CommunicationStyle.Technical))
    .Build();

var architectureExpert = new AgentBuilder()
    .WithId("architecture")
    .WithName("Software Architect")
    .WithProvider(provider)
    .WithCustomPersonality(p => p
        .WithName("Software Architect")
        .WithSystemPrompt(@"You are a principal software architect with expertise in clean architecture.
Focus on: SOLID principles, design patterns, separation of concerns, testability,
extensibility, code organization, and long-term maintainability.
Consider how changes affect the broader system.")
        .WithExpertise("Software Architecture")
        .WithCreativity(0.5)
        .WithPrecision(0.85)
        .WithStyle(CommunicationStyle.Professional))
    .Build();

var pragmatist = new AgentBuilder()
    .WithId("pragmatist")
    .WithName("Pragmatic Developer")
    .WithProvider(provider)
    .WithCustomPersonality(p => p
        .WithName("Pragmatic Developer")
        .WithSystemPrompt(@"You are a senior developer who balances idealism with practical delivery.
Focus on: code readability, team conventions, documentation, error handling,
edge cases, and whether the code actually solves the problem at hand.
Push back on over-engineering while ensuring quality.")
        .WithExpertise("Practical Software Development")
        .WithCreativity(0.6)
        .WithPrecision(0.8)
        .WithStyle(CommunicationStyle.Direct))
    .Build();

var sampleCode = @"
```csharp
// UserService.cs - Handles user authentication and profile management
public class UserService
{
    private readonly string _connectionString;
    private readonly ILogger _logger;

    public UserService(string connectionString, ILogger logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task<User?> AuthenticateAsync(string username, string password)
    {
        var query = $""SELECT * FROM Users WHERE Username = '{username}' AND Password = '{password}'"";

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        using var command = new SqlCommand(query, connection);
        using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new User
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                Email = reader.GetString(3),
                Role = reader.GetString(4)
            };
        }

        _logger.LogInformation($""Failed login attempt for user: {username}"");
        return null;
    }

    public async Task<List<User>> GetAllUsersAsync()
    {
        var users = new List<User>();
        var query = ""SELECT * FROM Users"";

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        using var command = new SqlCommand(query, connection);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            users.Add(new User
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                Password = reader.GetString(2),  // Include password for admin view
                Email = reader.GetString(3),
                Role = reader.GetString(4)
            });
        }

        return users;
    }

    public void UpdateUserPassword(int userId, string newPassword)
    {
        var query = $""UPDATE Users SET Password = '{newPassword}' WHERE Id = {userId}"";

        using var connection = new SqlConnection(_connectionString);
        connection.Open();

        using var command = new SqlCommand(query, connection);
        command.ExecuteNonQuery();
    }

    public async Task<bool> IsAdminAsync(string username)
    {
        // Cache admin status for performance
        if (AdminCache.ContainsKey(username))
            return AdminCache[username];

        var query = $""SELECT Role FROM Users WHERE Username = '{username}'"";

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        using var command = new SqlCommand(query, connection);
        var role = await command.ExecuteScalarAsync() as string;

        var isAdmin = role == ""Admin"";
        AdminCache[username] = isAdmin;

        return isAdmin;
    }

    private static Dictionary<string, bool> AdminCache = new();
}
```
";

var reviewPrompt = $@"
Review the following code pull request. Analyze it from your area of expertise and provide a structured assessment.

{sampleCode}

Respond with a JSON object matching this exact structure:
{{
    ""Summary"": ""Brief overall assessment of the code"",
    ""Overall"": {{
        ""CodeQuality"": <1-10>,
        ""Security"": <1-10>,
        ""Performance"": <1-10>,
        ""Maintainability"": <1-10>,
        ""TestCoverage"": <1-10>
    }},
    ""Findings"": [
        {{
            ""Severity"": ""Critical|High|Medium|Low"",
            ""Category"": ""Security|Performance|Architecture|BestPractice"",
            ""Description"": ""What the issue is"",
            ""Location"": ""Where in the code"",
            ""Recommendation"": ""How to fix it""
        }}
    ],
    ""Strengths"": [""List of positive aspects""],
    ""SuggestedImprovements"": [""Prioritized list of improvements""],
    ""ApprovalRecommended"": true/false,
    ""ConfidenceScore"": <1-100>
}}

Focus on your area of expertise but provide a complete assessment.";

Console.WriteLine("Code under review:");
Console.WriteLine("─".PadRight(60, '─'));
Console.WriteLine("UserService.cs - Authentication and user management service");
Console.WriteLine("─".PadRight(60, '─'));
Console.WriteLine();

var workflow = Workflow.Create<CodeReviewResult>()
    .WithName("Multi-Agent Code Review")
    .AddAgents(new[] { securityExpert, performanceExpert, architectureExpert, pragmatist })
    .WithAggregation()
    .WithArbiter(provider)
    .Build();

var options = new WorkflowOptions
{
    EnableParallelExecution = true,
    Timeout = TimeSpan.FromMinutes(3),
    OnProgress = progress =>
    {
        var icon = progress.Stage switch
        {
            WorkflowStage.Initializing => "🔄",
            WorkflowStage.AgentProcessing => "🔍",
            WorkflowStage.Voting => "🗳️",
            WorkflowStage.Finalizing => "✨",
            WorkflowStage.Completed => "✅",
            _ => "⏳"
        };
        Console.WriteLine($"{icon} {progress.Message}");
    }
};

Console.WriteLine("Starting multi-agent code review...\n");

var result = await workflow.ExecuteAsync(reviewPrompt, options);

Console.WriteLine();
Console.WriteLine("═".PadRight(60, '═'));
Console.WriteLine("                    REVIEW RESULTS                    ");
Console.WriteLine("═".PadRight(60, '═'));
Console.WriteLine();

if (!result.IsSuccess)
{
    Console.WriteLine($"Review failed: {result.Error}");
    return;
}

Console.WriteLine("Individual Agent Assessments:");
Console.WriteLine("─".PadRight(60, '─'));

foreach (var response in result.AgentResponses)
{
    Console.WriteLine($"\n📋 {response.AgentName}");
    Console.WriteLine($"   Response time: {response.ResponseTime.TotalSeconds:F1}s");

    if (response.StructuredOutput is CodeReviewResult agentResult)
    {
        Console.WriteLine($"   Approval: {(agentResult.ApprovalRecommended ? "✅ Recommended" : "❌ Not Recommended")}");
        Console.WriteLine($"   Confidence: {agentResult.ConfidenceScore}%");
        Console.WriteLine($"   Findings: {agentResult.Findings.Count} issues found");
    }
}

Console.WriteLine();
Console.WriteLine("═".PadRight(60, '═'));
Console.WriteLine("              AGGREGATED FINAL VERDICT                ");
Console.WriteLine("═".PadRight(60, '═'));

if (result.Value != null)
{
    var final = result.Value;

    Console.WriteLine($"\n📝 Summary: {final.Summary}\n");

    Console.WriteLine("Overall Scores (1-10):");
    Console.WriteLine($"   Code Quality:    {"█".PadRight(final.Overall.CodeQuality, '█').PadRight(10, '░')} {final.Overall.CodeQuality}/10");
    Console.WriteLine($"   Security:        {"█".PadRight(final.Overall.Security, '█').PadRight(10, '░')} {final.Overall.Security}/10");
    Console.WriteLine($"   Performance:     {"█".PadRight(final.Overall.Performance, '█').PadRight(10, '░')} {final.Overall.Performance}/10");
    Console.WriteLine($"   Maintainability: {"█".PadRight(final.Overall.Maintainability, '█').PadRight(10, '░')} {final.Overall.Maintainability}/10");
    Console.WriteLine($"   Test Coverage:   {"█".PadRight(final.Overall.TestCoverage, '█').PadRight(10, '░')} {final.Overall.TestCoverage}/10");

    if (final.Findings.Any())
    {
        Console.WriteLine("\n🔍 Key Findings:");
        var grouped = final.Findings.GroupBy(f => f.Severity).OrderBy(g => g.Key switch
        {
            "Critical" => 0,
            "High" => 1,
            "Medium" => 2,
            _ => 3
        });

        foreach (var group in grouped)
        {
            var icon = group.Key switch
            {
                "Critical" => "🔴",
                "High" => "🟠",
                "Medium" => "🟡",
                _ => "🟢"
            };

            foreach (var finding in group)
            {
                Console.WriteLine($"\n   {icon} [{finding.Severity}] {finding.Category}");
                Console.WriteLine($"      {finding.Description}");
                Console.WriteLine($"      📍 {finding.Location}");
                Console.WriteLine($"      💡 {finding.Recommendation}");
            }
        }
    }

    if (final.Strengths.Any())
    {
        Console.WriteLine("\n✨ Strengths:");
        foreach (var strength in final.Strengths)
        {
            Console.WriteLine($"   • {strength}");
        }
    }

    if (final.SuggestedImprovements.Any())
    {
        Console.WriteLine("\n📈 Suggested Improvements:");
        for (int i = 0; i < final.SuggestedImprovements.Count; i++)
        {
            Console.WriteLine($"   {i + 1}. {final.SuggestedImprovements[i]}");
        }
    }

    Console.WriteLine();
    Console.WriteLine("─".PadRight(60, '─'));
    var verdict = final.ApprovalRecommended ? "✅ APPROVED" : "❌ CHANGES REQUESTED";
    Console.WriteLine($"Final Verdict: {verdict} (Confidence: {final.ConfidenceScore}%)");
    Console.WriteLine("─".PadRight(60, '─'));
}

if (result.VotingResult != null)
{
    Console.WriteLine($"\nConsensus Score: {result.VotingResult.ConsensusScore:P0}");
    Console.WriteLine($"Strategy Used: {result.VotingResult.StrategyUsed}");
}

Console.WriteLine($"\nTotal execution time: {result.ExecutionTime.TotalSeconds:F1}s");
Console.WriteLine();
