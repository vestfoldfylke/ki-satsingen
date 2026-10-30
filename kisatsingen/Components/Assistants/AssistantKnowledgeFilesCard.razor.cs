using kisatsingen.Authorization;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services;
using kisatsingen.Services.Assistants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;

namespace kisatsingen.Components.Assistants;

// An assistant's knowledge files: the list for everyone who can use it, and
// adding files when CanEditAssistant allows. Adding checks the policy again,
// since a picker that was allowed when it rendered may no longer be.
//
// Shows one assistant for its whole life: its files load once, and its
// uploads go to that assistant until the card is disposed, which resets them.
public sealed partial class AssistantKnowledgeFilesCard : ComponentBase, IDisposable
{
    [Inject]
    public required IKnowledgeFileRepository KnowledgeFileRepository { get; init; }

    [Inject]
    public required IAuthenticationService AuthenticationService { get; init; }

    [Inject]
    public required IAuthorizationService AuthorizationService { get; init; }

    [Inject]
    public required AssistantFileUploads Uploads { get; init; }

    [CascadingParameter]
    public required Task<AuthenticationState> AuthenticationState { get; set; }

    [Parameter, EditorRequired]
    public required Assistant Assistant { get; set; }

    private IReadOnlyList<KnowledgeFileMetadata> _files = [];
    private bool _isLoading = true;
    private string? _refusal;

    private bool IsFull => _files.Count >= Assistant.MaxKnowledgeFiles;

    protected override async Task OnInitializedAsync()
    {
        Uploads.Changed += OnUploadsChanged;
        await LoadFilesAsync();
        _isLoading = false;
    }

    private async Task LoadFilesAsync()
    {
        var userId = await AuthenticationService.RequireUserObjectIdentifierAsync();
        _files = await KnowledgeFileRepository.ListFilesForAssistantAsync(userId, Assistant.Id);
    }

    private async Task AddFilesAsync(InputFileChangeEventArgs selection)
    {
        var user = (await AuthenticationState).User;
        if (!(await AuthorizationService.AuthorizeAsync(user, Assistant, AuthorizationPolicies.CanEditAssistant)).Succeeded)
        {
            _refusal = "Du har ikke lenger tilgang til å legge til filer i denne assistenten.";
            return;
        }

        _refusal = null;
        await Uploads.AddAsync(Assistant.Id, selection, _files.Count, LoadFilesAsync);
    }

    private static string DescribeStatus(AssistantFileUpload upload) => upload.Status switch
    {
        AssistantFileUploadStatus.Waiting => "Venter",
        AssistantFileUploadStatus.Uploading => $"Laster opp {upload.UploadedPercent} %",
        AssistantFileUploadStatus.Processing => "Behandles …",
        AssistantFileUploadStatus.Rejected => upload.RejectionReason ?? "Kunne ikke legges til",
        _ => upload.Status.ToString()
    };

    private void OnUploadsChanged() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        Uploads.Changed -= OnUploadsChanged;
        Uploads.Reset();
    }
}
