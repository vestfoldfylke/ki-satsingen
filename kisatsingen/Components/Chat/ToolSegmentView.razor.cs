using Microsoft.AspNetCore.Components;
using kisatsingen.Services.Chat;

namespace kisatsingen.Components.Chat;

public partial class ToolSegmentView : ComponentBase
{
    [Parameter, EditorRequired] public required ToolSegment Segment { get; set; }

    private ToolCallView? _view;

    // Built once per segment, not per render: a result can be pages of text.
    private ToolCallView View
    {
        get
        {
            if (_view is null || _view.Segment != Segment)
            {
                _view = ToolCallView.Build(Segment);
            }

            return _view;
        }
    }

    private string OutcomeColor => View.Outcome switch
    {
        ToolOutcome.Refused => "warning",
        ToolOutcome.Failed => "danger",
        _ => "neutral"
    };
}
