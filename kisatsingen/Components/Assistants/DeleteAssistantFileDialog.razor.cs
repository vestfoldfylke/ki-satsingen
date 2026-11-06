using kisatsingen.Authorization;
using kisatsingen.Components.Shared;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace kisatsingen.Components.Assistants;

// Asks before deleting one of an assistant's files. It has no button of its
// own: it opens from a command="show-modal" button whose commandfor is Id, and
// Load says which file. Deleting checks the policy again, since a dialog that
// was allowed when it rendered may no longer be.
public sealed partial class DeleteAssistantFileDialog : ComponentBase
{
    [Inject]
    public required IKnowledgeFileRepository KnowledgeFileRepository { get; init; }

    [Inject]
    public required IAuthenticationService AuthenticationService { get; init; }

    [Inject]
    public required IAuthorizationService AuthorizationService { get; init; }

    [CascadingParameter]
    public required Task<AuthenticationState> AuthenticationState { get; set; }

    [Parameter, EditorRequired]
    public required Assistant Assistant { get; set; }

    [Parameter, EditorRequired]
    public required string Id { get; set; }

    // Raised once the file is gone: deleted here, or already deleted elsewhere.
    [Parameter]
    public EventCallback OnDeleted { get; set; }

    private ModalDialog? _dialog;
    private KnowledgeFileMetadata? _file;
    private bool _isNotFound;
    private string? _refusal;

    public void Load(KnowledgeFileMetadata file)
    {
        _file = file;
        _isNotFound = false;
        _refusal = null;
        StateHasChanged();
    }

    private async Task DeleteAsync()
    {
        if (_file is not { } file)
        {
            return;
        }

        var user = (await AuthenticationState).User;
        if (!(await AuthorizationService.AuthorizeAsync(user, Assistant, AuthorizationPolicies.CanEditAssistant)).Succeeded)
        {
            _refusal = "Du har ikke lenger tilgang til å slette filer i denne assistenten.";
            return;
        }

        var userId = await AuthenticationService.RequireUserObjectIdentifierAsync();
        if (!await KnowledgeFileRepository.DeleteAssistantFileAsync(userId, Assistant.Id, file.Id))
        {
            _isNotFound = true;
            await OnDeleted.InvokeAsync();
            return;
        }

        _dialog?.Close();
        await OnDeleted.InvokeAsync();
    }
}
