using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
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

    // Held while files upload, since picking again would make the files still
    // uploading from the last pick unreadable. Not held during a turn: a file
    // attached now goes with the next message.
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
        // Checked before any await, so a quick double send (e.g. Enter and a click
        // at the same time) is turned away before the text is read from the browser.
        // Uploading is checked here even though the browser already blocks sending
        // during an upload, since the browser's check can't be relied on.
        // Session.CanSend is checked so that a message the session would refuse
        // stays in the composer and OnSending is not raised.
        if (_isSending || Attachments.IsUploading || !Session.CanSend)
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

    private async Task AttachQueuedFilesAsync(InputFileChangeEventArgs selection)
    {
        try
        {
            await Attachments.AttachAsync(selection);
        }
        finally
        {
            await MarkSelectionReadAsync();
        }
    }

    // Lets the browser put the next pasted selection on the file input.
    // Every interop failure is caught: an exception thrown from an event handler
    // ends the circuit, so a connection that dropped for a moment would not be
    // able to reconnect.
    private async Task MarkSelectionReadAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("chatClient.markComposerSelectionRead");
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or OperationCanceledException)
        {
            Logger.LogWarning(ex, "ChatComposer could not tell the browser the pasted files were read, so further pasted files wait until another chat is opened or the page is reloaded");
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

                // A message sent with the button leaves focus on the button, which
                // the turn disables; the textarea gets it back so the next message
                // can be typed without clicking.
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
