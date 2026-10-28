using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace kisatsingen.Components.Layout;

public partial class NavMenu : ComponentBase, IDisposable
{
    private const string SidebarPanelId = "sidebar-panel";
    private const string UserMenuId = "user-menu-dropdown";
    private static readonly string[] PopoverIds = [SidebarPanelId, UserMenuId];

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

    private IReadOnlyList<ChatListEntry> ChatList => Manager.Entries;
    private bool _stateWired;

    private async Task HidePopoversAsync()
    {
        foreach (var popoverId in PopoverIds)
        {
            try
            {
                await using var popover = await JS.InvokeAsync<IJSObjectReference>("document.getElementById", popoverId);
                await popover.InvokeVoidAsync("hidePopover");
            }
            catch (JSDisconnectedException)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to hide {PopoverId} after navigation; it stays open", popoverId);
            }
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!_stateWired)
        {
            Session.StateChanged += OnStateChanged;
            Manager.ChatListChanged += OnStateChanged;
            Navigation.LocationChanged += OnLocationChanged;
            _stateWired = true;
        }

        await Manager.EnsureLoadedAsync();
    }

    private async void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        await HidePopoversAsync();
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

        // Before the delete, so the turn's tail can't append to a row that is gone.
        if (wasActive)
        {
            await Session.StopTurnForLeaveAsync();
        }

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
            // Replaced: back must not lead to the deleted chat.
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
            Navigation.LocationChanged -= OnLocationChanged;
        }
    }
}
