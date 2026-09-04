using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Layout;

public partial class NavMenu : ComponentBase, IDisposable
{
    [Inject]
    public required ChatSession Session { get; set; }

    [Inject]
    public required IChatRepository ChatRepository { get; set; }

    [Inject]
    public required ILogger<NavMenu> Logger { get; set; }

    [Inject]
    public required NavigationManager Navigation { get; set; }

    [Parameter]
    public Guid? ChatId { get; set; }

    private string MessageText { get; set; } = string.Empty;
    private List<ChatSummary> ChatList { get; set; } = [];
    private bool _stateWired;

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

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (_stateWired)
        {
            Session.StateChanged -= OnSessionChanged;
        }
        Session.Cancel();
    }
}
