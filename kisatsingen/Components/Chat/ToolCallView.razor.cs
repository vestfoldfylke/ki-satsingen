using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
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

    // Formatted once per segment, not per render: a result can be pages of text.
    private ToolSegment? _formattedFor;
    private string _arguments = string.Empty;
    private string? _result;

    // Mapped explicitly, so renaming a member can't silently rename a CSS hook.
    private string StatusName => Segment.Status switch
    {
        ToolStatus.Running => "running",
        ToolStatus.Completed => "completed",
        ToolStatus.Failed => "failed",
        ToolStatus.Interrupted => "interrupted",
        _ => "unknown"
    };

    private string StatusText => Segment.Status switch
    {
        ToolStatus.Running => "Kjører …",
        ToolStatus.Completed => "Ferdig",
        ToolStatus.Failed => "Feilet",
        ToolStatus.Interrupted => "Avbrutt",
        _ => "Ukjent"
    };

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(_formattedFor, Segment))
        {
            return;
        }

        _formattedFor = Segment;
        _arguments = FormatArguments(Segment.Arguments);
        _result = Segment.Result is { } result ? FormatResult(result) : null;
    }

    // Undefined when a stored segment has no arguments at all.
    private static string FormatArguments(JsonElement arguments) =>
        arguments.ValueKind == JsonValueKind.Undefined ? string.Empty : JsonSerializer.Serialize(arguments, IndentedJson);

    private static string FormatResult(string result)
    {
        if (result.AsSpan().TrimStart() is not ['{' or '[', ..])
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(result);
            return JsonSerializer.Serialize(document.RootElement, IndentedJson);
        }
        catch (JsonException)
        {
            return result;
        }
    }
}
