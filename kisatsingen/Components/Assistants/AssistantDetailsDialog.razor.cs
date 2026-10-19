using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace kisatsingen.Components.Assistants;

// An assistant's name, description and visibility, editable when
// CanEditAssistant allows. What is shown follows AuthorizeView; saving checks
// the policies again, since a dialog that was allowed when it rendered may no
// longer be.
public sealed partial class AssistantDetailsDialog : ComponentBase
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
    public required Guid AssistantId { get; set; }

    // After a save, so the caller can show the change.
    [Parameter]
    public EventCallback OnChanged { get; set; }

    private string DialogId => $"assistant-details-{AssistantId}";

    private AssistantDetailsForm Form { get; set; } = new();

    private Assistant? _assistant;
    private bool _isLoading;
    private bool _isNotFound;
    private string? _refusal;

    private int _dialogKey;

    private async Task OpenAsync()
    {
        _refusal = null;
        _isNotFound = false;

        _isLoading = true;
        var userId = await AuthenticationService.RequireUserObjectIdentifierAsync();
        _assistant = await AssistantRepository.GetAssistantAsync(userId, AssistantId);
        _isLoading = false;

        if (_assistant is null)
        {
            _isNotFound = true;
            return;
        }

        Form = AssistantDetailsForm.From(_assistant);
    }

    private async Task SaveAsync()
    {
        if (_assistant is null)
        {
            return;
        }

        var user = (await AuthenticationState).User;

        if (Form.Visibility == AssistantVisibility.Organization
            && !(await AuthorizationService.AuthorizeAsync(user, "CanContributeOrganizationWide")).Succeeded)
        {
            _refusal = "Du har ikke tilgang til å lage organisasjons-delte assistenter.";
            return;
        }

        if (!(await AuthorizationService.AuthorizeAsync(user, _assistant, "CanEditAssistant")).Succeeded)
        {
            _refusal = "Du har ikke tilgang til å redigere denne assistenten.";
            return;
        }

        var userId = await AuthenticationService.RequireUserObjectIdentifierAsync();
        if (!await AssistantRepository.UpdateAssistantDetailsAsync(userId, _assistant.Id, Form.Name, Form.Description, Form.Visibility))
        {
            _isNotFound = true;
            return;
        }

        CloseDialog();
        await OnChanged.InvokeAsync();
    }

    // Closing a modal <dialog> from code takes its close() method, which only
    // JavaScript can call. A new key makes Blazor replace the element instead,
    // and a new <dialog> starts closed.
    private void CloseDialog() => _dialogKey++;
}
