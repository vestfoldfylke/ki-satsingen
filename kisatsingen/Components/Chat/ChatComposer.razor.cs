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

    // Empty for a frame while the page reads the catalogue, rather than blocking
    // the composer on it.
    [Parameter]
    public IReadOnlyList<ChatModel> Models { get; set; } = [];

    [Parameter]
    public ChatModel? SelectedModel { get; set; }

    [Parameter]
    public EventCallback<ChatModelKey> OnSelectModel { get; set; }

    [Parameter]
    public ChatModel? PendingModel { get; set; }

    [Parameter]
    public long? EstimatedContextTokens { get; set; }

    // The page's switch from empty state to transcript layout rebuilds this DOM
    // and drops the caret; it calls this to restore focus.
    public ValueTask FocusAsync() => _textarea.FocusAsync();

    // One call, not a read and a clear: see takeComposerValue in chat-composer.ts.
    public ValueTask<string> TakeTextAsync() =>
        JS.InvokeAsync<string>("chatClient.takeComposerValue");

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            // Once is enough: a new textarea always comes with a new instance.
            if (firstRender)
            {
                await JS.InvokeVoidAsync("chatClient.initComposer");
            }

            // Re-rendered on every session change, but IsBusy only flips at turn
            // start and end — don't resend what the client already has.
            if (firstRender || IsBusy != _previousIsBusy)
            {
                var completingTurn = _previousIsBusy == true && !IsBusy;
                _previousIsBusy = IsBusy;
                await JS.InvokeVoidAsync("chatClient.setComposerBusy", IsBusy);

                // The textarea was disabled during the turn and lost focus; give it
                // back so the next message can be typed without clicking.
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
