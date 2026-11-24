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
    private bool _isDarkMode;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        await ReadColorSchemeAsync();
    }

    private async Task ReadColorSchemeAsync()
    {
        try
        {
            var current = await JS.InvokeAsync<string?>("document.documentElement.getAttribute", "data-color-scheme");

            _isDarkMode = current == "dark";
        }
        catch (JSDisconnectedException)
        {
            return;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to read the saved color scheme; the dark mode switch shows off");
        }

        StateHasChanged();
    }

    private async Task ApplyColorSchemeAsync()
    {
        var scheme = _isDarkMode ? "dark" : "light";

        try
        {
            await JS.InvokeVoidAsync("document.documentElement.setAttribute", "data-color-scheme", scheme);
        }
        catch (JSDisconnectedException)
        {
            return;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to apply color scheme {Scheme}; the page keeps its current theme", scheme);
            return;
        }

        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", "color-scheme", scheme);
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone; nothing left to save to.
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to save color scheme {Scheme}; it resets on next page load", scheme);
        }
    }

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

    protected override void OnInitialized()
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
