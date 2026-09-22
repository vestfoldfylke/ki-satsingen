using kisatsingen.Services.Chat;
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

    [Parameter]
    public EventCallback OnStop { get; set; }

    // Empty until the page has read the catalogue, which it does asynchronously —
    // so this renders one frame without a picker rather than blocking the composer
    // on a round trip the user is not waiting for.
    [Parameter]
    public IReadOnlyList<ChatModel> Models { get; set; } = [];

    [Parameter]
    public ChatModel? SelectedModel { get; set; }

    [Parameter]
    public EventCallback<ChatModelKey> OnSelectModel { get; set; }

    // Null unless a switch is waiting to take effect; see ChatSession.PendingModel.
    [Parameter]
    public ChatModel? PendingModel { get; set; }

    // The conversation's current size; see ChatSession.EstimatedContextTokens.
    [Parameter]
    public long? EstimatedContextTokens { get; set; }

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
                var completingTurn = _previousIsBusy == true && !IsBusy;
                _previousIsBusy = IsBusy;
                await JS.InvokeVoidAsync("chatClient.setComposerBusy", IsBusy);

                // A busy→idle transition means the turn just ended (natural
                // completion or Stop). The textarea was disabled during the
                // turn, so focus is nowhere useful — put it back so the next
                // message can be typed without clicking.
                if (completingTurn)
                {
                    await _textarea.FocusAsync();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "ChatComposer JS interop failed");
        }
    }
}
