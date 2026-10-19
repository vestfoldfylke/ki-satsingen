using System.ComponentModel.DataAnnotations;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace kisatsingen.Components.Assistants;

// An assistant's instructions: visible to everyone who can use it, editable
// when CanEditAssistant allows. Saving checks the policy again, since a dialog
// that was allowed when it rendered may no longer be.
public sealed partial class AssistantInstructionsDialog : ComponentBase
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

    private string DialogId => $"assistant-instructions-{AssistantId}";

    private InstructionsForm Form { get; set; } = new();

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

        Form = new InstructionsForm { Instructions = _assistant.Instructions };
    }

    private async Task SaveAsync()
    {
        if (_assistant is null)
        {
            return;
        }

        var user = (await AuthenticationState).User;
        if (!(await AuthorizationService.AuthorizeAsync(user, _assistant, "CanEditAssistant")).Succeeded)
        {
            _refusal = "Du har ikke lenger tilgang til å redigere denne assistenten.";
            return;
        }

        var userId = await AuthenticationService.RequireUserObjectIdentifierAsync();
        if (!await AssistantRepository.UpdateAssistantInstructionsAsync(userId, _assistant.Id, Form.Instructions))
        {
            _isNotFound = true;
            return;
        }

        CloseDialog();
    }

    // Closing a modal <dialog> from code takes its close() method, which only
    // JavaScript can call. A new key makes Blazor replace the element instead,
    // and a new <dialog> starts closed.
    private void CloseDialog() => _dialogKey++;

    // The entity's own limit, so the form refuses what the repository would.
    private sealed class InstructionsForm
    {
        // In ErrorMessage - {0} = FieldName, {1} = MaximumLength, {2} = MinimumLength (if set)
        [StringLength(Assistant.MaxInstructionsLength, ErrorMessage = "Instruksjonene kan ha opptil {1} tegn.")]
        public string? Instructions { get; set; }
    }
}
