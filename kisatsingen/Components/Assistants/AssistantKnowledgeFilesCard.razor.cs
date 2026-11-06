using kisatsingen.Authorization;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services;
using kisatsingen.Services.Assistants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace kisatsingen.Components.Assistants;

// An assistant's knowledge files: the list and viewing a file for everyone who
// can use it, and adding and deleting files when CanEditAssistant allows. Adding
// checks the policy again, since a picker that was allowed when it rendered may
// no longer be; DeleteAssistantFileDialog does the same for deleting.
//
// Shows one assistant for its whole life: its uploads go to that assistant
// until the card is disposed, which resets them.
public sealed partial class AssistantKnowledgeFilesCard : ComponentBase, IDisposable
{
    [Inject]
    public required IAuthorizationService AuthorizationService { get; init; }

    [Inject]
    public required AssistantFileUploads Uploads { get; init; }

    [Inject]
    public required IKnowledgeFileRepository KnowledgeFileRepository { get; init; }

    [Inject]
    public required IAuthenticationService AuthenticationService { get; init; }

    [Inject]
    public required IJSRuntime JS { get; init; }

    [Inject]
    public required ILogger<AssistantKnowledgeFilesCard> Logger { get; init; }

    [CascadingParameter]
    public required Task<AuthenticationState> AuthenticationState { get; set; }

    [Parameter, EditorRequired]
    public required Assistant Assistant { get; set; }

    [Parameter, EditorRequired]
    public required IReadOnlyList<KnowledgeFileMetadata> Files { get; set; }

    // Raised once a file is saved or deleted.
    [Parameter]
    public EventCallback OnChanged { get; set; }

    private string? _refusal;
    private ElementReference _menu;
    private DeleteAssistantFileDialog? _deleteDialog;

    // The file in the view dialog; it stays set after the dialog is closed.
    private KnowledgeFileMetadata? _shownFile;

    private bool IsFull => Files.Count >= Assistant.MaxKnowledgeFiles;

    private string MenuId => $"assistant-files-menu-{Assistant.Id}";

    private string DeleteDialogId => $"delete-assistant-file-dialog-{Assistant.Id}";

    private string ViewerDialogId => $"assistant-file-viewer-dialog-{Assistant.Id}";

    protected override void OnInitialized() => Uploads.Changed += OnUploadsChanged;

    private void ShowFile(KnowledgeFileMetadata file) => _shownFile = file;

    private async Task<KnowledgeFileText?> ReadFileTextAsync(Guid fileId, CancellationToken ct)
    {
        var userId = await AuthenticationService.RequireUserObjectIdentifierAsync();
        return await KnowledgeFileRepository.GetAssistantFileTextAsync(userId, Assistant.Id, fileId, ct);
    }

    private async Task AddFilesAsync(InputFileChangeEventArgs selection)
    {
        await HideMenuAsync();

        var user = (await AuthenticationState).User;
        if (!(await AuthorizationService.AuthorizeAsync(user, Assistant, AuthorizationPolicies.CanEditAssistant)).Succeeded)
        {
            _refusal = "Du har ikke lenger tilgang til å legge til filer i denne assistenten.";
            return;
        }

        _refusal = null;
        await Uploads.AddAsync(Assistant.Id, selection, savedFileCount: Files.Count, onFileSaved: OnChanged.InvokeAsync);
    }

    // Selecting files is a click inside the menu, which popover="auto" doesn't
    // treat as a dismissal, so the menu would stay open over the progress.
    private async Task HideMenuAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("chatClient.hidePopover", _menu);
        }
        catch (JSException ex)
        {
            Logger.LogWarning(ex, "AssistantKnowledgeFilesCard could not close its menu after a file selection");
        }
    }

    private static string DescribeStatus(AssistantFileUpload upload) => upload.Status switch
    {
        AssistantFileUploadStatus.Waiting => "Venter",
        AssistantFileUploadStatus.Uploading => $"Laster opp {upload.UploadedPercent} %",
        AssistantFileUploadStatus.Processing => "Behandles …",
        AssistantFileUploadStatus.Rejected => upload.RejectionReason ?? "Kunne ikke legges til",
        _ => upload.Status.ToString()
    };

    // Uploads change in the middle of AddFilesAsync, where Blazor doesn't render by itself.
    private void OnUploadsChanged() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        Uploads.Changed -= OnUploadsChanged;
        Uploads.Reset();
    }
}
