using kisatsingen.Components.Chat;
using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace kisatsingen.Components.Pages;

public sealed partial class Chat : ComponentBase, IAsyncDisposable
{
    [Inject]
    public required ChatSession Session { get; set; }

    [Inject]
    public required ChatAttachments Attachments { get; set; }

    [Inject]
    public required ChatManager Manager { get; set; }

    [Inject]
    public required ILogger<Chat> Logger { get; set; }

    [Inject]
    public required NavigationManager Navigation { get; set; }

    [Inject]
    public required IJSRuntime JS { get; set; }

    [Parameter]
    public Guid? ChatId { get; set; }

    private string MessageText { get; set; } = string.Empty;
    private ChatComposer? _composer;
    private bool _stateWired;
    private Guid? _lastInitChatId;
    private bool _lastInitChatHadVisibleMessages;
    private bool _isFilesPanelOpen;

    // From the sidebar's list rather than a load of its own: that list already
    // tracks renames and the title a new chat is given. Null for a new chat.
    private string? Title =>
        Manager.Entries.FirstOrDefault(entry => entry.Id == Session.ChatId)?.Title;

    private void ToggleFilesPanel() => _isFilesPanelOpen = !_isFilesPanelOpen;

    protected override async Task OnParametersSetAsync()
    {
        if (!_stateWired)
        {
            Session.StateChanged += OnSessionChanged;
            Session.ChatCreated += OnChatCreated;
            Attachments.Changed += OnSessionChanged;
            Manager.ChatListChanged += OnSessionChanged;
            _stateWired = true;
        }

        if (Session.ChatId == ChatId)
        {
            return;
        }

        if (ChatId is Guid chatId)
        {
            await Session.OpenChatAsync(chatId);
        }
        else
        {
            await Session.StartNewChatAsync();
        }
    }

    private void OnSessionChanged() => InvokeAsync(StateHasChanged);

    // Replaced, not pushed: /new has nothing to go back to. The same page instance
    // serves both routes and the session already holds this chat, so nothing reloads.
    // Rechecked because it runs later: by then the user may have left the chat.
    private void OnChatCreated(Guid chatId) =>
        InvokeAsync(() =>
        {
            if (ChatId is null && Session.ChatId == chatId)
            {
                Navigation.NavigateTo($"/chat/{chatId}", replace: true);
            }
        });

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var chatSwitched = _lastInitChatId != Session.ChatId;
        var visibilityFlipped = _lastInitChatHadVisibleMessages != Session.HasVisibleMessages;
        if (!firstRender && !chatSwitched && !visibilityFlipped)
        {
            return;
        }

        // Sending the first message swaps the empty-state layout for the
        // transcript layout. They're separate branches, so the composer's DOM
        // is rebuilt and the caret is lost — restore it below.
        var becameActive = !firstRender && !_lastInitChatHadVisibleMessages && Session.HasVisibleMessages;

        _lastInitChatId = Session.ChatId;
        _lastInitChatHadVisibleMessages = Session.HasVisibleMessages;

        try
        {
            await JS.InvokeVoidAsync("chatClient.initChatLog");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "JS interop failed for {Method}", "chatClient.initChatLog");
        }

        if (becameActive && _composer is not null)
        {
            await _composer.FocusAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_stateWired)
        {
            Session.StateChanged -= OnSessionChanged;
            Session.ChatCreated -= OnChatCreated;
            Attachments.Changed -= OnSessionChanged;
            Manager.ChatListChanged -= OnSessionChanged;
        }
        // No cancel: this also runs on circuit teardown, where the session's own
        // disposal records the disconnect. A cancel here would label it wrongly.
        return ValueTask.CompletedTask;
    }
}
