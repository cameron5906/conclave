using FluentAssertions;
using Conclave.Tools;

namespace Conclave.Tests.Tools;

public class ToolBuilderTests
{
    [Fact]
    public void Build_CreatesToolWithBasicProperties()
    {
        var tool = new ToolBuilder()
            .WithName("search")
            .WithDescription("Search for information")
            .Build();

        tool.Name.Should().Be("search");
        tool.Description.Should().Be("Search for information");
    }

    [Fact]
    public void WithStringParameter_AddsStringProperty()
    {
        var tool = new ToolBuilder()
            .WithName("search")
            .WithDescription("Search for information")
            .WithStringParameter("query", "The search query", required: true)
            .Build();

        tool.Parameters.Properties.Should().ContainKey("query");
        tool.Parameters.Properties["query"].Type.Should().Be("string");
        tool.Parameters.Properties["query"].Description.Should().Be("The search query");
        tool.Parameters.Required.Should().Contain("query");
    }

    [Fact]
    public void WithNumberParameter_AddsNumberProperty()
    {
        var tool = new ToolBuilder()
            .WithName("calculate")
            .WithDescription("Perform calculation")
            .WithNumberParameter("value", "The numeric value")
            .Build();

        tool.Parameters.Properties.Should().ContainKey("value");
        tool.Parameters.Properties["value"].Type.Should().Be("number");
    }

    [Fact]
    public void WithBooleanParameter_AddsBooleanProperty()
    {
        var tool = new ToolBuilder()
            .WithName("toggle")
            .WithDescription("Toggle a setting")
            .WithBooleanParameter("enabled", "Whether to enable")
            .Build();

        tool.Parameters.Properties.Should().ContainKey("enabled");
        tool.Parameters.Properties["enabled"].Type.Should().Be("boolean");
    }

    [Fact]
    public void WithArrayParameter_AddsArrayProperty()
    {
        var tool = new ToolBuilder()
            .WithName("process")
            .WithDescription("Process items")
            .WithArrayParameter("items", "List of items", "string")
            .Build();

        tool.Parameters.Properties.Should().ContainKey("items");
        tool.Parameters.Properties["items"].Type.Should().Be("array");
        tool.Parameters.Properties["items"].Items!.Type.Should().Be("string");
    }

    [Fact]
    public async Task WithHandler_ExecutesHandler()
    {
        var tool = new ToolBuilder()
            .WithName("greet")
            .WithDescription("Say hello")
            .WithHandler(async (args) => ToolResult.Ok($"Hello, {args}!"))
            .Build();

        var result = await tool.Handler!("World", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Be("Hello, World!");
    }

    [Fact]
    public void WithEnumParameter_AddsEnumValues()
    {
        var tool = new ToolBuilder()
            .WithName("select")
            .WithDescription("Select an option")
            .WithParameter("option", "string", "The option to select", required: true,
                enumValues: new List<string> { "a", "b", "c" })
            .Build();

        tool.Parameters.Properties["option"].Enum.Should().Contain("a", "b", "c");
    }
}
