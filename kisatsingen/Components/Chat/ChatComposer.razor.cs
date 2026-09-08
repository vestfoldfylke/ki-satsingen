using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace kisatsingen.Components.Chat;

public sealed partial class ChatComposer : ComponentBase
{
    private ElementReference _textarea;

    [Parameter]
    public required string Text { get; set; }

    [Parameter]
    public EventCallback<string> TextChanged { get; set; }

    [Parameter]
    public bool IsBusy { get; set; }

    [Parameter]
    public EventCallback OnSend { get; set; }

    // The page switches between an empty-state layout and the scrolling
    // transcript layout, which rebuilds this component's DOM and drops the
    // caret. The page calls this afterwards to put focus back.
    public ValueTask FocusAsync() => _textarea.FocusAsync();

    private Task OnInputAsync(ChangeEventArgs e) =>
        TextChanged.InvokeAsync(e.Value as string ?? string.Empty);

    // Enter sends, Shift+Enter inserts a newline. The composer owns its own
    // keyboard contract so callers only have to handle "send".
    private async Task OnKeyDownAsync(KeyboardEventArgs e)
    {
        if (e is { Key: "Enter", ShiftKey: false })
        {
            await OnSend.InvokeAsync();
        }
    }
}
