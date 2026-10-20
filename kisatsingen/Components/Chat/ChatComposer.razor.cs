using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace kisatsingen.Components.Chat;

public sealed partial class ChatComposer : ComponentBase, IDisposable
{
    private ElementReference _textarea;
    private bool? _previousIsBusy;
    private bool? _previousIsUploading;
    private bool _isSending;

    [Inject]
    public required ChatSession Session { get; set; }

    [Inject]
    public required ChatAttachments Attachments { get; set; }

    [Inject]
    public required IJSRuntime JS { get; set; }

    [Inject]
    public required ILogger<ChatComposer> Logger { get; set; }

    // A new selection makes the files of the one still uploading unreadable. A
    // running turn does not hold the picker: the files a message takes are
    // decided at send, so one attached now goes with the next message.
    private bool IsPickerDisabled => Attachments.IsUploading;

    private sealed record ComposerNotice(string Color, string Text);

    private ComposerNotice? Notice
    {
        get
        {
            var model = Session.SelectedModel;
            return model.ClassifyContext(Session.EstimatedContextTokens) switch
            {
                ContextFillLevel.Overflowing => new("danger", $"Samtalen er trolig for lang for {model.DisplayName}. Svaret kan feile. Start en ny samtale eller bytt modell."),
                ContextFillLevel.NearlyFull => new("danger", $"Samtalen er nesten full for {model.DisplayName}. Start en ny samtale snart."),
                ContextFillLevel.Filling => new("warning", $"Samtalen nærmer seg grensen for {model.DisplayName}."),
                _ => null,
            };
        }
    }

    // Raised just before a message is sent to the chat session.
    [Parameter]
    public EventCallback OnSending { get; set; }

    // Re-renders itself when the session or the attachments change: the composer
    // reads their state directly rather than receiving it as parameters.
    protected override void OnInitialized()
    {
        Session.StateChanged += OnStateChanged;
        Attachments.Changed += OnStateChanged;
    }

    private async Task SendAsync()
    {
        // Synchronous, checked before any await: a rapid double-trigger (e.g.
        // Enter racing a click) is rejected here at zero network cost, rather
        // than after a wasted round trip to read the composer's text.
        // Uploading is checked too: the browser holds send while it lasts, and
        // this is the guard that does not trust it.
        if (_isSending || Attachments.IsUploading)
        {
            return;
        }

        _isSending = true;
        try
        {
            // One call, not a read and a clear: see takeComposerValue in chat-composer.ts.
            var text = await JS.InvokeAsync<string>("chatClient.takeComposerValue");
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            await OnSending.InvokeAsync();

            await Session.SendAsync(text);
        }
        finally
        {
            _isSending = false;
        }
    }

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
            var isBusy = Session.IsBusy;
            if (firstRender || isBusy != _previousIsBusy)
            {
                var completingTurn = _previousIsBusy == true && !isBusy;
                _previousIsBusy = isBusy;
                await JS.InvokeVoidAsync("chatClient.setComposerBusy", isBusy);

                // The textarea was disabled during the turn and lost focus; give it
                // back so the next message can be typed without clicking.
                if (completingTurn)
                {
                    await _textarea.FocusAsync();
                }
            }

            var isUploading = Attachments.IsUploading;
            if (firstRender || isUploading != _previousIsUploading)
            {
                _previousIsUploading = isUploading;
                await JS.InvokeVoidAsync("chatClient.setComposerUploading", isUploading);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "ChatComposer JS interop failed");
        }
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        Session.StateChanged -= OnStateChanged;
        Attachments.Changed -= OnStateChanged;
    }
}
