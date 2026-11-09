using System.Text.Json;

namespace kisatsingen.AIFunctions;

// What a tool answers when it refuses a call: the reason, written to the model
// so it can correct the call.
internal sealed record ToolError(string Error)
{
    // Error as it is written: tool results are serialised in camelCase.
    private const string ErrorProperty = "error";

    public static bool IsRefusal(JsonElement result) =>
        result.ValueKind == JsonValueKind.Object
        && result.TryGetProperty(ErrorProperty, out var error)
        && error.ValueKind == JsonValueKind.String;
}
