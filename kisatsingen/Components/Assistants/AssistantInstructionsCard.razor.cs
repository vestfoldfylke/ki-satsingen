using System.ComponentModel.DataAnnotations;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace kisatsingen.Components.Assistants;

// An assistant's instructions: a preview for everyone who can use it, all of
// them in a dialog, editable there when CanEditAssistant allows. Saving checks
// the policy again, since a dialog that was allowed when it rendered may no
// longer be.
public sealed partial class AssistantInstructionsCard : ComponentBase
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
    public required Assistant Assistant { get; set; }

    // Raised once the instructions are saved.
    [Parameter]
    public EventCallback OnChanged { get; set; }

    private string DialogId => $"assistant-instructions-{Assistant.Id}";

    private InstructionsForm Form { get; set; } = new();

    private bool _isNotFound;
    private string? _refusal;

    private int _dialogKey;

    private void OpenDialog()
    {
        _refusal = null;
        _isNotFound = false;
        Form = new InstructionsForm { Instructions = Assistant.Instructions };
    }

    private async Task SaveAsync()
    {
        var user = (await AuthenticationState).User;
        if (!(await AuthorizationService.AuthorizeAsync(user, Assistant, "CanEditAssistant")).Succeeded)
        {
            _refusal = "Du har ikke lenger tilgang til å redigere denne assistenten.";
            return;
        }

        var userId = await AuthenticationService.RequireUserObjectIdentifierAsync();
        if (!await AssistantRepository.UpdateAssistantInstructionsAsync(userId, Assistant.Id, Form.Instructions))
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

    // The entity's own limit, so the form refuses what the repository would.
    private sealed class InstructionsForm
    {
        // In ErrorMessage - {0} = FieldName, {1} = MaximumLength, {2} = MinimumLength (if set)
        [StringLength(Assistant.MaxInstructionsLength, ErrorMessage = "Instruksjonene kan ha opptil {1} tegn.")]
        public string? Instructions { get; set; }
    }
}
