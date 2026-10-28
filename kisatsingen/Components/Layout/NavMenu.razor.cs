using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace kisatsingen.Components.Layout;

public partial class NavMenu : ComponentBase, IDisposable
{
    private const int TooltipOnChatListTitleLengthTreshold = 45;

    private const string NewChatPath = "new";
    private const string SidebarPanelId = "sidebar-panel";

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
        try
        {
            await using var sidebarPanel = await JS.InvokeAsync<IJSObjectReference>("document.getElementById", SidebarPanelId);
            await sidebarPanel.InvokeVoidAsync("hidePopover");
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone, so there is no page left to hide the panel on.
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to hide {SidebarPanelId} after navigation; the mobile drawer stays open", SidebarPanelId);
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
