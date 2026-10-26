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

        var wasActive = Session.ChatId == chat.Id;

        try
        {  
            if(wasActive)
            {
                await Session.StopTurnForLeaveAsync();
            }

            await Manager.DeleteAsync(chat.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to delete chat {ChatId}", chat.Id);
            return;
        }

        _dialog?.Close();
        Chat = null;

        if (wasActive && Session.ChatId == chat.Id)
        {
            Session.Reset();
            Navigation.NavigateTo($"/{NewChatPath}", replace: true);   
        }
    }
}
