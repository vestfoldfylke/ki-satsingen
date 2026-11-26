using kisatsingen.AIFunctions.WebSearch;
using kisatsingen.Authorization;
using kisatsingen.Components.Shared;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace kisatsingen.Components.Assistants;

// The tools an assistant's owner chooses: shown to everyone who can use it,
// edited in a dialog when CanEditAssistant allows. Saving checks the policy
// again, since a dialog that was allowed when it rendered may no longer be.
public sealed partial class AssistantToolsCard : ComponentBase
{
    [Inject]
    public required IAssistantRepository AssistantRepository { get; init; }

    [Inject]
    public required IAuthenticationService AuthenticationService { get; init; }

    [Inject]
    public required IAuthorizationService AuthorizationService { get; init; }

    [Inject]
    public required SearchWebToolFactory SearchWebTools { get; init; }

    [CascadingParameter]
    public required Task<AuthenticationState> AuthenticationState { get; set; }

    [Parameter, EditorRequired]
    public required Assistant Assistant { get; set; }

    // Raised once the tools are saved.
    [Parameter]
    public EventCallback OnChanged { get; set; }

    private string DialogId => $"assistant-tools-{Assistant.Id}";

    private ToolsForm Form { get; set; } = new();

    private bool _isNotFound;
    private string? _refusal;

    private ModalDialog? _dialog;

    // Without a search model, the search setting is hidden: it would change nothing.
    private bool IsWebSearchAvailable => SearchWebTools.IsAvailable;

    private static string DescribeSetting(bool isEnabled) => isEnabled ? "På" : "Av";

    // Off is a choice, not an error, so neutral rather than a warning colour.
    private static string ChooseSettingColor(bool isEnabled) => isEnabled ? "success" : "neutral";

    // Both settings, search too while its checkbox is hidden, since saving
    // writes both: a hidden one left out would be saved as off.
    private void OpenDialog()
    {
        _refusal = null;
        _isNotFound = false;
        Form = new ToolsForm
        {
            IsFileGenerationEnabled = Assistant.IsFileGenerationEnabled,
            IsWebSearchEnabled = Assistant.IsWebSearchEnabled
        };
    }

    private async Task SaveAsync()
    {
        var user = (await AuthenticationState).User;
        if (!(await AuthorizationService.AuthorizeAsync(user, Assistant, AuthorizationPolicies.CanEditAssistant)).Succeeded)
        {
            _refusal = "Du har ikke lenger tilgang til å redigere denne assistenten.";
            return;
        }

        var userId = await AuthenticationService.RequireUserObjectIdentifierAsync();
        var isSaved = await AssistantRepository.UpdateAssistantToolsAsync(
            userId,
            Assistant.Id,
            isFileGenerationEnabled: Form.IsFileGenerationEnabled,
            isWebSearchEnabled: Form.IsWebSearchEnabled);
        if (!isSaved)
        {
            _isNotFound = true;
            return;
        }

        _dialog?.Close();
        await OnChanged.InvokeAsync();
    }

    private sealed class ToolsForm
    {
        public bool IsFileGenerationEnabled { get; set; }

        public bool IsWebSearchEnabled { get; set; }
    }
}
