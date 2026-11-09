using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using kisatsingen.AIFunctions;
using kisatsingen.Services.Chat;

namespace kisatsingen.Components.Chat;

public partial class ToolCallView : ComponentBase
{
    // Relaxed, or every æ, ø and å is shown as an escape sequence. Safe because
    // Blazor HTML-encodes the text it renders; this JSON never reaches a script.
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [Parameter, EditorRequired] public required ToolSegment Segment { get; set; }

    // Worked out once per segment, not per render: a result can be pages of text.
    private ToolSegment? _formattedFor;
    private ToolOutcome _outcome;
    private string _title = string.Empty;
    private string _arguments = string.Empty;
    private string? _result;

    private string OutcomeColor => _outcome switch
    {
        ToolOutcome.Refused => "warning",
        ToolOutcome.Failed => "danger",
        _ => "neutral"
    };

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(_formattedFor, Segment))
        {
            return;
        }

        _formattedFor = Segment;
        var resultJson = ParseResult(Segment.Result);
        _outcome = ClassifyOutcome(Segment.Status, resultJson);
        _title = ToolCallTitle.Describe(Segment.ToolName, _outcome, resultJson);
        _arguments = FormatArguments(Segment.Arguments);
        _result = resultJson is { } json ? JsonSerializer.Serialize(json, IndentedJson) : Segment.Result;
    }

    private static ToolOutcome ClassifyOutcome(ToolStatus status, JsonElement? result) => status switch
    {
        ToolStatus.Running => ToolOutcome.Running,
        ToolStatus.Completed when result is { } json && ToolError.IsRefusal(json) => ToolOutcome.Refused,
        ToolStatus.Completed => ToolOutcome.Succeeded,
        ToolStatus.Failed => ToolOutcome.Failed,
        ToolStatus.Interrupted => ToolOutcome.Interrupted,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "A tool status with no outcome.")
    };

    // Undefined when a stored segment has no arguments at all.
    private static string FormatArguments(JsonElement arguments) =>
        arguments.ValueKind == JsonValueKind.Undefined ? string.Empty : JsonSerializer.Serialize(arguments, IndentedJson);

    // Null unless the result is a JSON object or array.
    private static JsonElement? ParseResult(string? result)
    {
        if (result is null || result.AsSpan().TrimStart() is not ['{' or '[', ..])
        {
            return null;
        }

        try
        {
            return JsonElement.Parse(result);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
