using System.Text.Json;
using System.Text.Json.Serialization;

namespace Conclave.Tools;

public class ToolDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public ToolParameters Parameters { get; init; } = new();
    public Func<string, CancellationToken, Task<ToolResult>>? Handler { get; init; }
}

public class ToolParameters
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, ToolProperty> Properties { get; init; } = new();

    [JsonPropertyName("required")]
    public List<string> Required { get; init; } = new();
}

public class ToolProperty
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "string";

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("enum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Enum { get; init; }

    [JsonPropertyName("items")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolProperty? Items { get; init; }
}

public class ToolResult
{
    public bool Success { get; init; }
    public string Output { get; init; } = string.Empty;
    public string? Error { get; init; }

    public static ToolResult Ok(string output) => new() { Success = true, Output = output };
    public static ToolResult Fail(string error) => new() { Success = false, Error = error };
}

public class ToolBuilder
{
    private readonly ToolDefinition _tool = new();
    private string _name = string.Empty;
    private string _description = string.Empty;
    private readonly ToolParameters _parameters = new();
    private Func<string, CancellationToken, Task<ToolResult>>? _handler;

    public ToolBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public ToolBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    public ToolBuilder WithParameter(string name, string type, string description, bool required = false, List<string>? enumValues = null)
    {
        _parameters.Properties[name] = new ToolProperty
        {
            Type = type,
            Description = description,
            Enum = enumValues
        };
        if (required)
        {
            _parameters.Required.Add(name);
        }
        return this;
    }

    public ToolBuilder WithStringParameter(string name, string description, bool required = false)
        => WithParameter(name, "string", description, required);

    public ToolBuilder WithNumberParameter(string name, string description, bool required = false)
        => WithParameter(name, "number", description, required);

    public ToolBuilder WithBooleanParameter(string name, string description, bool required = false)
        => WithParameter(name, "boolean", description, required);

    public ToolBuilder WithArrayParameter(string name, string description, string itemType, bool required = false)
    {
        _parameters.Properties[name] = new ToolProperty
        {
            Type = "array",
            Description = description,
            Items = new ToolProperty { Type = itemType }
        };
        if (required)
        {
            _parameters.Required.Add(name);
        }
        return this;
    }

    public ToolBuilder WithHandler(Func<string, CancellationToken, Task<ToolResult>> handler)
    {
        _handler = handler;
        return this;
    }

    public ToolBuilder WithHandler(Func<string, Task<ToolResult>> handler)
    {
        _handler = (args, _) => handler(args);
        return this;
    }

    public ToolBuilder WithHandler<TArgs>(Func<TArgs, CancellationToken, Task<ToolResult>> handler) where TArgs : class
    {
        _handler = async (args, ct) =>
        {
            var parsed = JsonSerializer.Deserialize<TArgs>(args);
            if (parsed == null)
            {
                return ToolResult.Fail("Failed to parse arguments");
            }
            return await handler(parsed, ct);
        };
        return this;
    }

    public ToolDefinition Build()
    {
        return new ToolDefinition
        {
            Name = _name,
            Description = _description,
            Parameters = _parameters,
            Handler = _handler
        };
    }
}
