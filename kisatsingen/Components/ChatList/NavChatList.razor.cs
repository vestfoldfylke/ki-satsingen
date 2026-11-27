using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace kisatsingen.Components.ChatList;

public sealed partial class NavChatList : ComponentBase, IDisposable
{
    private const int MaxListedChats = 15;
    private const int TooltipTitleLengthThreshold = 20;
    private const string RenameChatDialogId = "nav-rename-chat-dialog";
    private const string DeleteChatDialogId = "nav-delete-chat-dialog";

    [Inject]
    public required ChatManager Manager { get; set; }

    [Inject]
    public required NavigationManager Navigation { get; set; }

    private RenameChatDialog? _renameDialog;
    private DeleteChatDialog? _deleteDialog;

    private IEnumerable<ChatListEntry> ListedChats => Manager.Entries.Take(MaxListedChats);

    private static string ChatPath(ChatListEntry entry) => $"/chat/{entry.Id}";

    private bool IsViewing(ChatListEntry entry) =>
        new Uri(Navigation.Uri).AbsolutePath.Equals(ChatPath(entry), StringComparison.OrdinalIgnoreCase);

    protected override async Task OnInitializedAsync()
    {
        Manager.ChatListChanged += OnChatListChanged;
        Navigation.LocationChanged += OnLocationChanged;

        await Manager.EnsureLoadedAsync();
    }

    private void OnChatListChanged() => InvokeAsync(StateHasChanged);

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args) => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        Manager.ChatListChanged -= OnChatListChanged;
        Navigation.LocationChanged -= OnLocationChanged;
    }
}
