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
    public required IKnowledgeFileRepository KnowledgeFileRepository { get; init; }

    [Inject]
    public required IAuthenticationService AuthenticationService { get; init; }

    [Inject]
    public required ChatSession Session { get; init; }

    [Inject]
    public required NavigationManager Navigation { get; init; }

    [Parameter]
    public Guid AssistantId { get; set; }

    private Assistant? _assistant;
    private IReadOnlyList<KnowledgeFileMetadata> _files = [];
    private bool _isLoading = true;

    protected override async Task OnParametersSetAsync()
    {
        await LoadAsync();

        if (_assistant is null)
        {
            return;
        }

        await Session.StartNewChatAsync(_assistant);
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
        var assistantId = AssistantId;
        var userId = await AuthenticationService.RequireUserObjectIdentifierAsync();
        var assistant = await AssistantRepository.GetAssistantAsync(userId, assistantId);
        var files = assistant is null ? [] : await KnowledgeFileRepository.ListFilesForAssistantAsync(userId, assistantId);

        // The page moved to another assistant meanwhile; that load assigns its own.
        if (assistantId != AssistantId)
        {
            return;
        }

        _assistant = assistant;
        _files = files;
        _isLoading = false;
    }

    private void OpenChat() => Navigation.NavigateTo($"/chat/{Session.ChatId}");
}
