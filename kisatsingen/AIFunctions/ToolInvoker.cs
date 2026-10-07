using System.Text.Json;
using Microsoft.Extensions.AI;

namespace kisatsingen.AIFunctions;

// FunctionInvokingChatClient.FunctionInvoker for every tool. Arguments the
// factory cannot bind are the model's mistake and it can fix them, so it is
// told what was wrong. Every other exception propagates: the client logs it and
// tells the model only that the function failed, so no internal detail reaches
// the chat. That is also why IncludeDetailedErrors stays off.
internal sealed class ToolInvoker(ILogger<ToolInvoker> logger)
{
    // AIFunctionFactory's ArgumentException for a missing required argument.
    private const string FactoryArgumentsParameter = "arguments";

    public async ValueTask<object?> InvokeAsync(FunctionInvocationContext context, CancellationToken cancellationToken)
    {
        var toolName = context.Function.Name;
        try
        {
            return await context.Function.InvokeAsync(context.Arguments, cancellationToken);
        }
        // Also caught when thrown by the tool itself, so the message is not
        // passed on: it could be the tool's, not the binder's. The binder's
        // says only which type a value failed to convert to, not which
        // argument, so the model loses little.
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Refused arguments for tool {ToolName}: a value could not be converted to its parameter's type.", toolName);
            return ArgumentError($"Argumentene til {toolName} kunne ikke leses: en verdi har feil type.", toolName);
        }
        // Filtered to the factory's own exception, whose message describes only
        // the arguments and names the missing one.
        catch (ArgumentException exception) when (exception.ParamName == FactoryArgumentsParameter)
        {
            logger.LogWarning("Refused arguments for tool {ToolName}: {Reason}", toolName, exception.Message);
            return ArgumentError($"Argumentene til {toolName} kunne ikke leses: {exception.Message}", toolName);
        }
    }

    private static string ArgumentError(string reason, string toolName) =>
        JsonSerializer.Serialize(
            new { error = $"{reason} Send argumentene med navn og typer slik skjemaet for {toolName} oppgir." },
            AIJsonUtilities.DefaultOptions);
}
