using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace kisatsingen.Components.Layout;

public partial class NavMenu : ComponentBase, IDisposable
{
    private const string SidebarPanelId = "sidebar-panel";
    private const string UserMenuId = "user-menu-dropdown";
    private static readonly string[] MenuIdsToHideOnNavigation = [SidebarPanelId, UserMenuId];

    [Inject]
    public required ILogger<NavMenu> Logger { get; set; }

    [Inject]
    public required NavigationManager Navigation { get; set; }

    [Inject]
    public required IJSRuntime JS { get; set; }

    private bool _chatsOpen = true;

    private async Task HideMenusAsync()
    {
        foreach (var menuId in MenuIdsToHideOnNavigation)
        {
            try
            {
                await using var menuElement = await JS.InvokeAsync<IJSObjectReference>("document.getElementById", menuId);
                await menuElement.InvokeVoidAsync("hidePopover");
            }
            catch (JSDisconnectedException)
            {
                // The circuit is gone, so there is no page left to hide the menus on.
                return;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to hide {MenuId} after navigation; it stays open", menuId);
            }
        }
    }

    protected override async Task OnInitializedAsync()
    {
        Navigation.LocationChanged += OnLocationChanged;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Navigation.LocationChanged -= OnLocationChanged;
    }

    private async void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        await HideMenusAsync();
    }
}
