using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Chat;

// ChatComposer bound to the chat session: sending, stopping, the model and the
// attachments. A page only decides where it goes.
public sealed partial class ChatSessionComposer : ComponentBase, IDisposable
{
    [Inject]
    public required ChatSession Session { get; set; }

    [Inject]
    public required ChatAttachments Attachments { get; set; }

    private ChatComposer? _composer;
    private bool _isSending;

    // Subscribed here, not left to the page: with no parameters of its own, this
    // component is not re-rendered when its page is.
    protected override void OnInitialized()
    {
        Session.StateChanged += OnStateChanged;
        Attachments.Changed += OnStateChanged;
    }

    public ValueTask FocusAsync() => _composer?.FocusAsync() ?? ValueTask.CompletedTask;

    private async Task SendAsync()
    {
        // Synchronous, checked before any await: a rapid double-trigger (e.g.
        // Enter racing a click) is rejected here at zero network cost, rather
        // than after a wasted round trip to read the composer's text.
        // Uploading is checked too: the browser holds send while it lasts, and
        // this is the guard that does not trust it.
        if (_isSending || _composer is null || Attachments.IsUploading)
        {
            return;
        }

        _isSending = true;
        try
        {
            var text = await _composer.TakeTextAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            await Session.SendAsync(text);
        }
        finally
        {
            _isSending = false;
        }
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        Session.StateChanged -= OnStateChanged;
        Attachments.Changed -= OnStateChanged;
    }
}
