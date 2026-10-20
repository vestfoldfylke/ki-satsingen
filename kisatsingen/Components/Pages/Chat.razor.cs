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
    public required IJSRuntime JS { get; set; }

    [Parameter]
    public Guid ChatIdFromUrl { get; set; }

    private bool _stateWired;
    private Guid? _previousChatIdFromUrl;
    private Guid? _lastInitChatId;
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
            Attachments.Changed += OnSessionChanged;
            Manager.ChatListChanged += OnSessionChanged;
            _stateWired = true;
        }
        
        if (ChatIdFromUrl == _previousChatIdFromUrl)
        {
            return;
        }

        _previousChatIdFromUrl = ChatIdFromUrl;

        if (Session.ChatId != ChatIdFromUrl)
        {
            await Session.OpenChatAsync(ChatIdFromUrl);
        }
    }

    private void OnSessionChanged() => InvokeAsync(StateHasChanged);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender && _lastInitChatId == Session.ChatId)
        {
            return;
        }

        _lastInitChatId = Session.ChatId;

        try
        {
            await JS.InvokeVoidAsync("chatClient.initChatLog");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "JS interop failed for {Method}", "chatClient.initChatLog");
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_stateWired)
        {
            Session.StateChanged -= OnSessionChanged;
            Attachments.Changed -= OnSessionChanged;
            Manager.ChatListChanged -= OnSessionChanged;
        }
        // No cancel: this also runs on circuit teardown, where the session's own
        // disposal records the disconnect. A cancel here would label it wrongly.
        return ValueTask.CompletedTask;
    }
}
