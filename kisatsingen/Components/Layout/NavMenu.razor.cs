using kisatsingen.Services;
using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace kisatsingen.Components.Layout;

public partial class NavMenu : ComponentBase, IDisposable
{
    [Inject]
    public required ChatSession Session { get; set; }

    [Inject]
    public required ChatManager Manager { get; set; }

    [Inject]
    public required ILogger<NavMenu> Logger { get; set; }

    [Inject]
    public required NavigationManager Navigation { get; set; }

    [Inject]
    public required IJSRuntime JS { get; set; }

    [Parameter]
    public Guid? ChatId { get; set; }

    private IReadOnlyList<ChatListEntry> ChatList => Manager.Entries;
    private bool _stateWired;

    protected override async Task OnParametersSetAsync()
    {
        if (!_stateWired)
        {
            Session.StateChanged += OnStateChanged;
            Manager.ChatListChanged += OnStateChanged;
            _stateWired = true;
        }

        if (Session.ChatId != ChatId)
        {
            await Session.LoadAsync(ChatId);
        }

        await Manager.EnsureLoadedAsync();
    }

    private void StartNewChatAsync()
    {
        if (Session.IsBusy)
        {
            return;
        }

        Logger.LogInformation("New chat started");
        Navigation.NavigateTo("/new", replace: true);
    }

    private async Task RenameChatAsync(Guid id, string currentTitle)
    {
        var input = await JS.InvokeAsync<string?>("prompt", "New name:", currentTitle);
        if (string.IsNullOrWhiteSpace(input) || input.Trim() == currentTitle)
        {
            return;
        }

        try
        {
            await Manager.RenameAsync(id, input.Trim(), CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to rename chat {ChatId}", id);
        }
    }

    private async Task DeleteChatAsync(Guid id)
    {
        var confirmed = await JS.InvokeAsync<bool>("confirm", "Delete this chat?");
        if (!confirmed)
        {
            return;
        }

        var wasActive = Session.ChatId == id;

        try
        {
            await Manager.DeleteAsync(id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to delete chat {ChatId}", id);
            return;
        }

        if (wasActive)
        {
            // Cancel first so an in-flight turn on this chat winds down before
            // Reset clears its identity — otherwise the tail of the turn tries
            // to append to a row that no longer exists.
            Session.Cancel();
            Session.Reset();
            Navigation.NavigateTo("/new", replace: true);
        }
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (_stateWired)
        {
            Session.StateChanged -= OnStateChanged;
            Manager.ChatListChanged -= OnStateChanged;
        }
        Session.Cancel();
    }
}
