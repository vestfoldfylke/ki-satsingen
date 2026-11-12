using System.Text.Json;
using System.Text.Json.Serialization;

namespace kisatsingen.AIFunctions;

// A tool's refusal, with the reason the model needs to correct its call.
// The JSON name is pinned so any serialiser writes what IsRefusal reads.
internal sealed record ToolError([property: JsonPropertyName(ToolError.ErrorProperty)] string Error)
{
    private const string ErrorProperty = "error";

    public static bool IsRefusal(JsonElement result) =>
        result.ValueKind == JsonValueKind.Object
        && result.TryGetProperty(ErrorProperty, out var error)
        && error.ValueKind == JsonValueKind.String;
}
