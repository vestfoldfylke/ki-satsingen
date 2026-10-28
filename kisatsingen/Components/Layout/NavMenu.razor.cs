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

    private RenameChatDialog? _renameDialog;
    private DeleteChatDialog? _deleteDialog;

    private async Task HideSidebarPanelAsync()
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
                // The circuit is gone, so there is no page left to hide the panel on.
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
        await HideSidebarPanelAsync();
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
