using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.ChatList;

public sealed partial class ChatOptionsMenu : ComponentBase
{
    // Unique per instance, so several menus for the same chat can be on one page.
    private readonly string _menuId = $"chat-options-{Guid.NewGuid():N}";

    [Parameter, EditorRequired]
    public required ChatListEntry Entry { get; set; }

    [Parameter, EditorRequired]
    public required string RenameDialogId { get; set; }

    [Parameter, EditorRequired]
    public required string DeleteDialogId { get; set; }

    [Parameter, EditorRequired]
    public required EventCallback<ChatListEntry> OnRenameRequested { get; set; }

    [Parameter, EditorRequired]
    public required EventCallback<ChatListEntry> OnDeleteRequested { get; set; }
}
