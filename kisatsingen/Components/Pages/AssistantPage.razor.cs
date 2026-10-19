using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Pages;

// Named …Page because "Assistant" is the entity.
public sealed partial class AssistantPage : ComponentBase
{
    [Inject]
    public required IAssistantRepository AssistantRepository { get; init; }

    [Inject]
    public required IAuthenticationService AuthenticationService { get; init; }

    [Parameter]
    public Guid AssistantId { get; set; }

    private Assistant? _assistant;
    private bool _isLoading = true;

    protected override Task OnParametersSetAsync() => LoadAsync();

    // Also for reloading after a save. Not shown as loading then: the current content
    // stays until the new arrives, rather than flickering away and back.
    private async Task LoadAsync()
    {
        var userId = await AuthenticationService.RequireUserObjectIdentifierAsync();
        _assistant = await AssistantRepository.GetAssistantAsync(userId, AssistantId);
        _isLoading = false;
    }
}
