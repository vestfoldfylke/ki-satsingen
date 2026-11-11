using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;
using kisatsingen.Components.Chat;

namespace kisatsingen.Components.Pages;

public sealed partial class Chats : ComponentBase, IDisposable
{
    private const int TooltipOnChatListTitleLengthThreshold = 80;

    [Inject]
    public required ChatManager Manager { get; set; }

    private IReadOnlyList<ChatListEntry> ChatList => Manager.Entries;

    private RenameChatDialog? _renameDialog;
    private DeleteChatDialog? _deleteDialog;

    protected override async Task OnInitializedAsync()
    {
        Manager.ChatListChanged += OnStateChanged;

        await Manager.EnsureLoadedAsync();
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    public void Dispose() => Manager.ChatListChanged -= OnStateChanged;
}
