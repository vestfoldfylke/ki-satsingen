using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace kisatsingen.Components.Layout;

public partial class NavMenu : ComponentBase, IDisposable
{
    private const string NewChatPath = "new";

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

    private bool _menuOpen;

    private void ToggleMenu()
    {
        _menuOpen = !_menuOpen;
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

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        if(!_menuOpen) return;
        _menuOpen = false;
        InvokeAsync(StateHasChanged);
    }

    private async Task StartNewChatAsync()
    {
        Logger.LogInformation("New chat started");

        _menuOpen = false;

        // Loaded here, not left to the page: until the first message has created
        // its chat the URL is still /new, and navigating to /new changes no
        // parameter, so the page would never load and that answer would carry on.
        await Session.LoadAsync(null);

        // Pushed: leaving a chat is a real navigation, and back should return to it.
        // Skipped on /new itself, where it would only stack duplicate entries.
        if (Navigation.ToBaseRelativePath(Navigation.Uri) != NewChatPath)
        {
            Navigation.NavigateTo($"/{NewChatPath}");
        }
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
            Session.Reset();
            // Replaced: back must not lead to the deleted chat.
            Navigation.NavigateTo($"/{NewChatPath}", replace: true);
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
