using Microsoft.AspNetCore.Components;
using kisatsingen.Services.Chat;

namespace kisatsingen.Components.Chat;

public partial class ToolCallBlock : ComponentBase
{
    [Parameter, EditorRequired] public required ToolCall ToolCall { get; set; }

    private ToolCallView? _view;
    private bool _isShowingRawData;

    // Built once per tool call, not per render: a result can be pages of text.
    private ToolCallView View
    {
        get
        {
            if (_view is null || !ReferenceEquals(_view.ToolCall, ToolCall))
            {
                _view = ToolCallView.Build(ToolCall);
            }

            return _view;
        }
    }

    private string ShownText => _isShowingRawData || View.UserFriendlyResult is null
        ? View.FormatToolCallJson()
        : View.UserFriendlyResult;

    private string OutcomeColor => View.Outcome switch
    {
        ToolOutcome.Refused => "warning",
        ToolOutcome.Failed => "danger",
        _ => "neutral"
    };
}
