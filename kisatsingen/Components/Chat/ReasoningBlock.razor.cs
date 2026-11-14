using Microsoft.AspNetCore.Components;
using kisatsingen.Services.Chat;

namespace kisatsingen.Components.Chat;

public partial class ReasoningBlock : ComponentBase
{
    [Parameter, EditorRequired] public required ReasoningSegment Segment { get; set; }

    [Parameter] public bool IsRunning { get; set; }
}
