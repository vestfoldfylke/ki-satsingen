using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace kisatsingen.Components.Assistants;

// An assistant's details (name, description, visibility), and for those who
// may edit it, editing them and deleting the assistant. Deleting checks the
// policy again, since a page that was allowed when it rendered may no longer be.
public sealed partial class AssistantDetails : ComponentBase
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

    // Raised once the details are saved.
    [Parameter]
    public EventCallback OnChanged { get; set; }

    private string DeletePopoverId => $"delete-assistant-{Assistant.Id}";

    private string? _refusal;

    private async Task DeleteAsync()
    {
        var user = (await AuthenticationState).User;
        if (!(await AuthorizationService.AuthorizeAsync(user, Assistant, "CanEditAssistant")).Succeeded)
        {
            _refusal = "Du har ikke tilgang til å slette denne assistenten.";
            return;
        }

        var userId = await AuthenticationService.RequireUserObjectIdentifierAsync();
        await AssistantRepository.DeleteAssistantAsync(userId, Assistant.Id);
        Navigation.NavigateTo(AssistantsPath);
    }
}
