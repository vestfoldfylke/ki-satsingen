using kisatsingen.Components.Shared;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace kisatsingen.Components.Assistants;

// Edits an assistant's name, description and visibility. Saving checks the
// policies again, since a dialog that was allowed when it rendered may no
// longer be.
public sealed partial class EditAssistantDetailsDialog : ComponentBase
{
    [Inject]
    public required IAssistantRepository AssistantRepository { get; init; }

    [Inject]
    public required IAuthenticationService AuthenticationService { get; init; }

    [Inject]
    public required IAuthorizationService AuthorizationService { get; init; }

    [CascadingParameter]
    public required Task<AuthenticationState> AuthenticationState { get; set; }

    [Parameter, EditorRequired]
    public required Assistant Assistant { get; set; }

    // Raised once the details are saved.
    [Parameter]
    public EventCallback OnChanged { get; set; }

    private string DialogId => $"edit-assistant-details-{Assistant.Id}";

    private AssistantDetailsForm Form { get; set; } = new();

    private bool _isNotFound;
    private string? _refusal;

    private ModalDialog? _dialog;

    private void OpenDialog()
    {
        _refusal = null;
        _isNotFound = false;
        Form = AssistantDetailsForm.From(Assistant);
    }

    private async Task SaveAsync()
    {
        var user = (await AuthenticationState).User;

        if (Form.Visibility == AssistantVisibility.Organization
            && !(await AuthorizationService.AuthorizeAsync(user, "CanContributeOrganizationWide")).Succeeded)
        {
            _refusal = "Assistenten er felles for hele organisasjonen, og du har ikke tilgang til å endre felles assistenter. Kontakt en administrator.";
            return;
        }

        if (!(await AuthorizationService.AuthorizeAsync(user, Assistant, "CanEditAssistant")).Succeeded)
        {
            _refusal = "Du har ikke tilgang til å redigere denne assistenten.";
            return;
        }

        var userId = await AuthenticationService.RequireUserObjectIdentifierAsync();
        if (!await AssistantRepository.UpdateAssistantDetailsAsync(userId, Assistant.Id, Form.Name, Form.Description, Form.Visibility))
        {
            _isNotFound = true;
            return;
        }

        _dialog?.Close();
        await OnChanged.InvokeAsync();
    }
}
