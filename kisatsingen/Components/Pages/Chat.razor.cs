using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace kisatsingen.Components.Pages;

public sealed partial class Chat : ComponentBase, IAsyncDisposable
{
    [Inject]
    public required ChatSession Session { get; set; }

    [Inject]
    public required IChatRepository ChatRepository { get; set; }

    [Inject]
    public required ILogger<Chat> Logger { get; set; }

    [Inject]
    public required NavigationManager Navigation { get; set; }

    [Inject]
    public required IJSRuntime JS { get; set; }

    [Parameter]
    public Guid? ChatId { get; set; }

    private string MessageText { get; set; } = string.Empty;
    private List<ChatSummary> ChatList { get; set; } = [];
    private bool _stateWired;
    private Guid? _lastInitChatId;
    private bool _lastInitHadVisible;

    protected override async Task OnParametersSetAsync()
    {
        if (!_stateWired)
        {
            Session.StateChanged += OnSessionChanged;
            _stateWired = true;
        }

        if (Session.ChatId != ChatId)
        {
            await Session.LoadAsync(ChatId);
        }

        await RefreshChatListAsync();
    }

    private async Task RefreshChatListAsync()
    {
        var list = await ChatRepository.ListChatsAsync(ownerId: null);
        ChatList = list.ToList();
    }

    private void StartNewChatAsync()
    {
        if (Session.IsBusy)
        {
            return;
        }

        Logger.LogInformation("New chat started");
        Navigation.NavigateTo("/chat", replace: true);
    }

    private async Task OnComposerKeyDownAsync(KeyboardEventArgs e)
    {
        if (e is { Key: "Enter", ShiftKey: false })
        {
            await SendAsync();
        }
    }

    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(MessageText))
        {
            return;
        }

        var text = MessageText;
        MessageText = string.Empty;
        var wasNew = Session.ChatId is null;

        await Session.SendAsync(text);

        if (wasNew && Session.ChatId is { } id)
        {
            Navigation.NavigateTo($"/chat/{id}", replace: true);
        }

        await RefreshChatListAsync();
    }

    private void OnSessionChanged() => InvokeAsync(StateHasChanged);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var chatSwitched = _lastInitChatId != Session.ChatId;
        var visibilityFlipped = _lastInitHadVisible != Session.HasVisibleMessages;
        if (!firstRender && !chatSwitched && !visibilityFlipped)
        {
            return;
        }

        _lastInitChatId = Session.ChatId;
        _lastInitHadVisible = Session.HasVisibleMessages;

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
        }
        // Cancel any in-flight send. Session is scoped, so DI owns its full
        // disposal — we don't call Session.DisposeAsync here.
        Session.Cancel();
        return ValueTask.CompletedTask;
    }
}
