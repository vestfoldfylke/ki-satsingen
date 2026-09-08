using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace kisatsingen.Components.Chat;

public sealed partial class ChatComposer : ComponentBase
{
    private ElementReference _textarea;
    private bool? _previousIsBusy;

    [Inject]
    public required IJSRuntime JS { get; set; }

    [Inject]
    public required ILogger<ChatComposer> Logger { get; set; }

    [Parameter]
    public bool IsBusy { get; set; }

    [Parameter]
    public EventCallback OnSend { get; set; }

    // The page switches between an empty-state layout and the scrolling
    // transcript layout, which rebuilds this component's DOM and drops the
    // caret. The page calls this afterwards to put focus back.
    public ValueTask FocusAsync() => _textarea.FocusAsync();

    // Reads and clears the textarea in one round trip — see chat-composer.ts's
    // takeComposerValue for why read+clear must happen atomically, not as two
    // separate calls.
    public ValueTask<string> TakeTextAsync() =>
        JS.InvokeAsync<string>("chatClient.takeComposerValue");

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            // A fresh DOM node always means a fresh component instance (see
            // FocusAsync's comment) — wiring once on firstRender is enough,
            // there's no later point where the textarea gets swapped out from
            // under this same instance.
            if (firstRender)
            {
                await JS.InvokeVoidAsync("chatClient.initComposer");
            }

            // This component re-renders on every streamed token (the page
            // re-renders as a whole while a response streams in), but IsBusy
            // itself only flips twice per turn — only push it to JS when it
            // actually changes, not on every render.
            if (firstRender || IsBusy != _previousIsBusy)
            {
                _previousIsBusy = IsBusy;
                await JS.InvokeVoidAsync("chatClient.setComposerBusy", IsBusy);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "JS interop failed for {Method}", "chatClient.initComposer/setComposerBusy");
        }
    }
}
