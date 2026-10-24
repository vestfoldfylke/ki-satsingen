using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services;
using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Pages;

// Named …Page because "Assistant" is the entity.
public sealed partial class AssistantPage : ComponentBase
{
    [Inject]
    public required IAssistantRepository AssistantRepository { get; init; }

    [Inject]
    public required IAuthenticationService AuthenticationService { get; init; }

    [Inject]
    public required ChatSession Session { get; init; }

    [Inject]
    public required NavigationManager Navigation { get; init; }

    [Parameter]
    public Guid AssistantId { get; set; }

    private Assistant? _assistant;
    private bool _isLoading = true;

    protected override async Task OnParametersSetAsync()
    {
        await LoadAsync();

        if (_assistant is not null && !Session.IsNewChatWith(_assistant.Id))
        {
            await Session.StartNewChatAsync(_assistant);
        }
    }

    private async Task ApplyEditAsync()
    {
        await LoadAsync();

        if (_assistant is not null)
        {
            Session.UpdateAssistant(_assistant);
        }
    }

    private async Task LoadAsync()
    {
        // No _isLoading true here, current stays until new arrives, to avoid flicker
        var userId = await AuthenticationService.RequireUserObjectIdentifierAsync();
        _assistant = await AssistantRepository.GetAssistantAsync(userId, AssistantId);
        _isLoading = false;
    }

    private void OpenChat() => Navigation.NavigateTo($"/chat/{Session.ChatId}");
}
