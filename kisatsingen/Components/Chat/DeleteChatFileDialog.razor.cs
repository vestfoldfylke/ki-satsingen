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

    // Stored rather than read at delete time: another chat can open before the
    // user confirms.
    private Guid _chatIdWhenOpened;

    public void Load(KnowledgeFileMetadata file)
    {
        _chatIdWhenOpened = Session.ChatId;
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

        if (!await Session.DeleteChatFileAsync(_chatIdWhenOpened, file.Id))
        {
            _isNotFound = true;
            return;
        }

        _dialog?.Close();
    }
}
