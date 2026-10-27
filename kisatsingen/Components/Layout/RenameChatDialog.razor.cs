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
    private string? _errorMessage;

    private ChatListEntry? Chat { get; set; }

    private RenameChatForm Form { get; set; } = new();

    private ModalDialog? _dialog;

    public void Load(ChatListEntry chat)
    {
        Chat = chat;
        Form = RenameChatForm.FromChat(chat);
        _isNotFound = false;
        _errorMessage = null;
        StateHasChanged();
    }

    private async Task SaveAsync()
    {
        _errorMessage = null;

        if (Chat is not { } chat)
        {
            return;
        }

        try
        {   
            if (!await Manager.RenameAsync(chat.Id, Form.Title, CancellationToken.None))
            {
                _isNotFound = true;
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to rename chat {ChatId}", chat.Id);
            _errorMessage = "Kunne ikke endre navn. Prøv igjen senere.";
            return;
        }

        _dialog?.Close();
        await OnChanged.InvokeAsync();
    }
}
