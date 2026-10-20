using kisatsingen.Authorization;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace kisatsingen.Components.Assistants;

// Asks before deleting an assistant, then goes to the assistant list. It has no
// button of its own: it opens from a command="show-modal" button whose
// commandfor is Id. Deleting checks the policy again, since a dialog that was
// allowed when it rendered may no longer be.
public sealed partial class DeleteAssistantDialog : ComponentBase
{
    private const string AssistantsPath = "/assistants";

    [Inject]
    public required IAssistantRepository AssistantRepository { get; init; }

    [Inject]
    public required IAuthenticationService AuthenticationService { get; init; }

    [Inject]
    public required IAuthorizationService AuthorizationService { get; init; }

    [Inject]
    public required NavigationManager Navigation { get; init; }

    [CascadingParameter]
    public required Task<AuthenticationState> AuthenticationState { get; set; }

    [Parameter, EditorRequired]
    public required Assistant Assistant { get; set; }

    [Parameter, EditorRequired]
    public required string Id { get; set; }

    private bool _isNotFound;
    private string? _refusal;

    private async Task DeleteAsync()
    {
        var user = (await AuthenticationState).User;
        if (!(await AuthorizationService.AuthorizeAsync(user, Assistant, AuthorizationPolicies.CanEditAssistant)).Succeeded)
        {
            _refusal = "Du har ikke tilgang til å slette denne assistenten.";
            return;
        }

        var userId = await AuthenticationService.RequireUserObjectIdentifierAsync();
        if (!await AssistantRepository.DeleteAssistantAsync(userId, Assistant.Id))
        {
            _isNotFound = true;
            return;
        }

        Navigation.NavigateTo(AssistantsPath);
    }
}
