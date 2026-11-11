using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;
using kisatsingen.Components.Shared;

namespace kisatsingen.Components.Chat;

public sealed partial class DeleteChatDialog : ComponentBase
{
    private const string NewChatPath = "new";

    private ModalDialog? _dialog;

    private string? _errorMessage;

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

    private bool IsViewing(Guid chatId) =>
    new Uri(Navigation.Uri).AbsolutePath
        .Equals($"/chat/{chatId}", StringComparison.OrdinalIgnoreCase);

    public void Load(ChatListEntry chat)
    {
        _errorMessage = null;
        Chat = chat;
        StateHasChanged();
    }

    private async Task DeleteAsync()
    {
        _errorMessage = null;

        if (Chat is not { } chat)
        {
            return;
        }

        // Stop before delete, so the turn cannot try to save to a deleted chat.
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
            _errorMessage = "Kunne ikke slette samtale. Prøv igjen senere.";
            return;
        }

        _dialog?.Close();

        // Reset to NewPlainChat, so back can't load deleted chat
        if (Session.ChatId == chat.Id)
        {
            await Session.StartNewPlainChatAsync();
        }

        // Replaced, so back doesn't lead to the deleted chat.
        if (IsViewing(chat.Id))
        {
            Navigation.NavigateTo($"/{NewChatPath}", replace: true);
        }
    }
}
