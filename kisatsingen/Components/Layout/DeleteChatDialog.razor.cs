using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;
using kisatsingen.Components.Shared;

namespace kisatsingen.Components.Layout;

public sealed partial class DeleteChatDialog : ComponentBase
{
    private const string NewChatPath = "new";

    private ModalDialog? _dialog;

    private ChatListEntry? Chat { get; set; }

    [Inject]
    public required ChatSession Session { get; set; }

    [Inject]
    public required ChatManager Manager { get; set; }

    [Inject]
    public required NavigationManager Navigation { get; init; }

    [Inject]
    public required ILogger<DeleteChatDialog> Logger { get; set; }

    [Parameter, EditorRequired]
    public required string Id { get; set; }

    public void Load(ChatListEntry chat)
    {
        Chat = chat;
        StateHasChanged();
    }

    private async Task DeleteAsync()
    {
        if (Chat is not { } chat)
        {
            return;
        }

        // Stopping active turn if the active chat is currently open
        if (Session.ChatId == chat.Id)
        {
            await Session.StopTurnForLeaveAsync();
        }

        try
        {
            await Manager.DeleteAsync(chat.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to delete chat {ChatId}", chat.Id);
            return;
        }

        _dialog?.Close();

        // Edge case: checking again after deletion incase the user
        // tries to create a new chat while deletion is in progress
        if (Session.ChatId == chat.Id)
        {
            Navigation.NavigateTo($"/{NewChatPath}", replace: true);
        }
    }
}
