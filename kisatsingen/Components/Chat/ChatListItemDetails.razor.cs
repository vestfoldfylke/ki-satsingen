using Microsoft.AspNetCore.Components;
using kisatsingen.Services.Chat;

namespace kisatsingen.Components.Chat;

public partial class ChatListItemDetails : ComponentBase
{
    [Parameter]
    public bool ShouldShowTime { get; set; }

    [Parameter, EditorRequired]
    public required string RenameDialogId { get; set; }

    [Parameter, EditorRequired]
    public required string DeleteDialogId { get; set; }

    [Parameter, EditorRequired]
    public EventCallback<ChatListEntry> OnRename { get; set; }

    [Parameter, EditorRequired]
    public EventCallback<ChatListEntry> OnDelete { get; set; }

    [Parameter, EditorRequired]
    public required string MenuId { get; set; }

    [Parameter, EditorRequired]
    public required ChatListEntry Entry { get; set; }
}
