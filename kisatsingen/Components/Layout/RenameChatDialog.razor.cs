using kisatsingen.Components.Shared;
using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Layout;

public sealed partial class RenameChatDialog : ComponentBase
{

    [Inject]
    public required ChatManager Manager { get; set; }

    [Inject]
    public required ILogger<RenameChatDialog> Logger { get; set; }

    [Parameter]
    public EventCallback OnChanged { get; set; }

    [Parameter, EditorRequired]
    public required string Id { get; set; }

    private bool _isNotFound;

    private ChatListEntry? Chat { get; set; }

    private RenameChatForm Form { get; set; } = new();

    private ModalDialog? _dialog;

    public void Load(ChatListEntry chat)
    {
        Chat = chat;
        Form = RenameChatForm.FromChat(chat);
        _isNotFound = false;
        StateHasChanged();
    }

    private async Task SaveAsync()
    {
        if (Chat is null)
        {
            return;
        }

        try
        {
            if(!await Manager.RenameAsync(Chat.Id, Form.Title, CancellationToken.None))
            {
                _isNotFound = true;
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to rename chat {ChatId}", Chat.Id);
            return;
        }

        _dialog?.Close();
        await OnChanged.InvokeAsync();
    }
}
