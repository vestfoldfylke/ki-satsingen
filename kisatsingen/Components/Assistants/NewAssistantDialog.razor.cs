using kisatsingen.Components.Shared;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace kisatsingen.Components.Assistants;

// Creates an assistant from its name, description and visibility; the
// instructions are written afterwards. Saving as organization-wide checks the
// policy again, since a dialog that was allowed when it rendered may no longer be.
public sealed partial class NewAssistantDialog : ComponentBase
{
    private const string DialogId = "new-assistant";

    [Inject]
    public required IAssistantRepository AssistantRepository { get; init; }

    [Inject]
    public required IAuthenticationService AuthenticationService { get; init; }

    [Inject]
    public required IAuthorizationService AuthorizationService { get; init; }

    [CascadingParameter]
    public required Task<AuthenticationState> AuthenticationState { get; set; }

    // Raised with the new assistant's id once it is created.
    [Parameter]
    public EventCallback<Guid> OnCreated { get; set; }

    private AssistantDetailsForm Form { get; set; } = new();

    private string? _refusal;

    private ModalDialog? _dialog;

    private void Open()
    {
        _refusal = null;
        Form = new AssistantDetailsForm();
    }

    private async Task CreateAsync()
    {
        var user = (await AuthenticationState).User;
        if (Form.Visibility == AssistantVisibility.Organization
            && !(await AuthorizationService.AuthorizeAsync(user, "CanContributeOrganizationWide")).Succeeded)
        {
            _refusal = "Du har ikke tilgang til å lage organisasjons-delte assistenter.";
            return;
        }

        var userId = await AuthenticationService.RequireUserObjectIdentifierAsync();
        var assistant = await AssistantRepository.CreateAssistantAsync(userId, Form.Name, Form.Description, Form.Visibility);

        _dialog?.Close();
        await OnCreated.InvokeAsync(assistant.Id);
    }
}
