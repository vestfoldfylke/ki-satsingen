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
        if (await LoadAsync() is Assistant assistant)
        {
            await Session.StartNewAssistantChatAsync(assistant, _files);
        }
    }

    private async Task ApplyChangeAsync()
    {
        if (await LoadAsync() is Assistant assistant)
        {
            Session.UpdateAssistant(assistant, _files);
        }
    }

    // Null when the assistant isn't found, or when the page moved to another
    // assistant meanwhile.
    private async Task<Assistant?> LoadAsync()
    {
        // No _isLoading true here, current stays until new arrives, to avoid flicker
        var assistantId = AssistantId;
        var userId = await AuthenticationService.RequireUserObjectIdentifierAsync();
        var assistant = await AssistantRepository.GetAssistantAsync(userId, assistantId);
        var files = assistant is null ? [] : await KnowledgeFileRepository.ListFilesForAssistantAsync(userId, assistantId);

        // Page has moved to another assistant, let that one load and set its own fields.
        if (assistantId != AssistantId)
        {
            return null;
        }

        _assistant = assistant;
        _files = files;
        _isLoading = false;
        return assistant;
    }

    private void OpenChat() => Navigation.NavigateTo($"/chat/{Session.ChatId}");
}
