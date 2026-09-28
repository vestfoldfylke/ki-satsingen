using System.Text.Json;
using Microsoft.AspNetCore.Components;
using kisatsingen.Services.Chat;

namespace kisatsingen.Components.Chat;

public partial class ToolCallView : ComponentBase
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    [Parameter, EditorRequired] public required ToolSegment Segment { get; set; }

    // Mapped explicitly, so renaming a member can't silently rename a CSS hook.
    private string StatusName => Segment.Status switch
    {
        ToolStatus.Running => "running",
        ToolStatus.Completed => "completed",
        ToolStatus.Failed => "failed",
        ToolStatus.Interrupted => "interrupted",
        _ => throw new ArgumentOutOfRangeException(nameof(Segment), Segment.Status, "Every tool status needs a CSS hook; add one to ToolCallView.")
    };

    private string StatusText => Segment.Status switch
    {
        ToolStatus.Running => "Kjører …",
        ToolStatus.Completed => "Ferdig",
        ToolStatus.Failed => "Feilet",
        ToolStatus.Interrupted => "Avbrutt",
        _ => throw new ArgumentOutOfRangeException(nameof(Segment), Segment.Status, "Every tool status needs a label; add one to ToolCallView.")
    };

    private static string FormatJson(JsonElement element) => JsonSerializer.Serialize(element, IndentedJson);
}
