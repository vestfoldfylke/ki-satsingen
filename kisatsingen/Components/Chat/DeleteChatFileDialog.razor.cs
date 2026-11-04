using kisatsingen.Components.Shared;
using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Chat;

// Asks before deleting one of the chat's files. It has no button of its own:
// it opens from a command="show-modal" button whose commandfor is Id, and Load
// says which file.
public sealed partial class DeleteChatFileDialog : ComponentBase
{
    [Inject]
    public required ChatSession Session { get; init; }

    [Parameter, EditorRequired]
    public required string Id { get; set; }

    private ModalDialog? _dialog;
    private KnowledgeFileMetadata? _file;
    private bool _isNotFound;

    // The chat the file was listed in. The page stays when the chat changes,
    // so the dialog can still be open in the next one.
    private Guid _chatId;

    public void Load(KnowledgeFileMetadata file)
    {
        _chatId = Session.ChatId;
        _file = file;
        _isNotFound = false;
        StateHasChanged();
    }

    private async Task DeleteAsync()
    {
        if (_file is not { } file)
        {
            return;
        }

        if (!await Session.DeleteChatFileAsync(_chatId, file.Id))
        {
            _isNotFound = true;
            return;
        }

        _dialog?.Close();
    }
}
