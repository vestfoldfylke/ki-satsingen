using kisatsingen.Components.ChatList;
using kisatsingen.Services.Chat;
using kisatsingen.Services.Cleanup;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Pages;

public sealed partial class Chats : ComponentBase, IDisposable
{
    private const int TooltipTitleLengthThreshold = 80;
    private const string RenameChatDialogId = "chats-rename-chat-dialog";
    private const string DeleteChatDialogId = "chats-delete-chat-dialog";

    [Inject]
    public required ChatManager Manager { get; set; }

    [Inject]
    public required DatabaseCleanupConfigurations CleanupConfigurations { get; set; }

    private RenameChatDialog? _renameDialog;
    private DeleteChatDialog? _deleteDialog;

    private string? DescribeUpcomingDeletion(ChatListEntry entry, DateTimeOffset utcNow) =>
        ChatRetention.DescribeUpcomingDeletion(entry.UpdatedAt, CleanupConfigurations.ChatCleanup, utcNow);

    protected override async Task OnInitializedAsync()
    {
        Manager.ChatListChanged += OnChatListChanged;

        await Manager.EnsureLoadedAsync();
    }

    private void OnChatListChanged() => InvokeAsync(StateHasChanged);

    public void Dispose() => Manager.ChatListChanged -= OnChatListChanged;
}
