using System.Text.Json;
using kisatsingen.AIFunctions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace kisatsingen.Tests.AIFunctions;

public sealed class ToolInvokerTests
{
    private static Task<object?> InvokeAsync(AIFunction function, AIFunctionArguments arguments) =>
        new ToolInvoker(NullLogger<ToolInvoker>.Instance).InvokeAsync(new FunctionInvocationContext { Function = function, Arguments = arguments }, CancellationToken.None).AsTask();

    [Fact]
    public async Task A_missing_required_argument_is_answered_with_its_name()
    {
        var function = AIFunctionFactory.Create((int count) => count, "count_things");

        var result = await InvokeAsync(function, []);

        Assert.Contains("'count'", Assert.IsType<string>(result));
    }

    [Fact]
    public async Task An_argument_of_the_wrong_type_is_answered_with_the_tool_name()
    {
        var function = AIFunctionFactory.Create((int count) => count, "count_things");

        var result = await InvokeAsync(function, new() { ["count"] = JsonSerializer.SerializeToElement("mange") });

        Assert.Contains("Argumentene til count_things kunne ikke leses", Assert.IsType<string>(result));
    }

    [Fact]
    public async Task A_json_exception_inside_the_tool_does_not_pass_its_message_to_the_model()
    {
        var function = AIFunctionFactory.Create(int () => throw new JsonException("Host=db.internal"), "parses_json");

        var result = await InvokeAsync(function, []);

        Assert.DoesNotContain("db.internal", Assert.IsType<string>(result));
    }

    [Fact]
    public async Task An_exception_inside_the_tool_propagates_so_no_internal_detail_reaches_the_model()
    {
        var function = AIFunctionFactory.Create(int () => throw new InvalidOperationException("connection string"), "broken");

        await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync(function, []));
    }
}
