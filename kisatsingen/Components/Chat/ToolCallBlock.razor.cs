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
            if (_view is null || _view.ToolCall != ToolCall)
            {
                _view = ToolCallView.Build(ToolCall);
            }

            return _view;
        }
    }

    // Without a user-friendly result there is nothing to toggle to, so the JSON is shown.
    private string ShownText => _isShowingRawData || View.UserFriendlyResult is null
        ? View.ToolCallJson
        : View.UserFriendlyResult;

    private string OutcomeColor => View.Outcome switch
    {
        ToolOutcome.Refused => "warning",
        ToolOutcome.Failed => "danger",
        _ => "neutral"
    };
}
