using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Pages;

public sealed partial class Assistants : ComponentBase
{
    [Inject]
    public required IAssistantRepository AssistantRepository { get; init; }

    [Inject]
    public required IAuthenticationService AuthenticationService { get; init; }

    [Inject]
    public required NavigationManager Navigation { get; init; }

    // What the user has made, and what the organization has: an
    // organization-wide assistant of the user's own is in both.
    private IReadOnlyList<AssistantSummary> Mine { get; set; } = [];

    private IReadOnlyList<AssistantSummary> OrganizationWide { get; set; } = [];

    private enum AssistantFilter
    {
        Mine,
        OrganizationWide
    }

    private AssistantFilter _filter = AssistantFilter.Mine;

    private IReadOnlyList<AssistantSummary> ShownAssistants =>
        _filter == AssistantFilter.Mine ? Mine : OrganizationWide;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        var userId = await AuthenticationService.RequireUserObjectIdentifierAsync();
        var assistants = await AssistantRepository.ListAssistantsAsync(userId);

        Mine = [.. assistants.Where(assistant => assistant.OwnerId == userId)];
        OrganizationWide = [.. assistants.Where(assistant => assistant.Visibility == AssistantVisibility.Organization)];
    }

    // A new assistant has no instructions yet; its page is where they are written.
    private void OpenAssistant(Guid assistantId) => Navigation.NavigateTo($"/assistants/{assistantId}");
}
