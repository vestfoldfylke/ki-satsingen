using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace kisatsingen.AIFunctions.FileTools;

// The file tools are AIFunctionFactory functions with one shared choice: how
// their result is written. Arguments the factory cannot bind are answered by
// ToolInvoker, the same as for every other tool.
internal static class FileToolFunction
{
    // Relaxed escaping comes from the defaults, so æ, ø and å stay one
    // character each instead of a six-character escape. The defaults also
    // indent, which only spends the model's tokens on whitespace.
    private static readonly JsonSerializerOptions ResultJson = CreateResultJson();

    public static AIFunction Create(string name, string description, Delegate method) =>
        AIFunctionFactory.Create(method, new AIFunctionFactoryOptions
        {
            Name = name,
            Description = description,
            MarshalResult = SerializeResult
        });

    // A string rather than the factory's JsonElement, so what is stored in the
    // turn and replayed is exactly what the model was sent.
    private static ValueTask<object?> SerializeResult(object? result, Type? _, CancellationToken __) =>
        ValueTask.FromResult<object?>(JsonSerializer.Serialize(result, result?.GetType() ?? typeof(object), ResultJson));

    private static JsonSerializerOptions CreateResultJson()
    {
        var options = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };
        options.MakeReadOnly();
        return options;
    }
}
