using kisatsingen.Components.Shared;
using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Layout;

public sealed partial class RenameChatDialog : ComponentBase
{
    [Inject]
    public required ChatSession Session { get; set; }

    [Inject]
    public required ChatManager Manager { get; set; }

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

        if(!await Manager.RenameAsync(Chat.Id, Form.Title, CancellationToken.None))
        {
            _isNotFound = true;
            return;
        }

        _dialog?.Close();
        Chat = null;
        await OnChanged.InvokeAsync();
    }
}
