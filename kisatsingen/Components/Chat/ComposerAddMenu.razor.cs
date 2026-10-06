using kisatsingen.Services;
using kisatsingen.Services.Attachments;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace kisatsingen.Components.Chat;

public sealed partial class ComposerAddMenu : ComponentBase
{
    // Fixed: only one composer is ever on screen.
    private const string PopoverId = "composer-add-menu-dropdown";
    private const string AlertId = "composer-add-menu-alert";
    private const string FilePickerNameId = "composer-add-menu-file-name";
    private const string FileKindsId = "composer-add-menu-file-kinds";
    private const string FileLimitsId = "composer-add-menu-file-limits";

    private ElementReference _popover;

    [Inject]
    public required IJSRuntime JS { get; set; }

    [Inject]
    public required AttachmentOptions Options { get; set; }

    [Inject]
    public required ILogger<ComposerAddMenu> Logger { get; set; }

    [Parameter]
    public bool IsFilePickerDisabled { get; set; }

    [Parameter]
    public EventCallback<InputFileChangeEventArgs> OnFilesSelected { get; set; }

    // The list is written to sit mid-sentence; here it starts the line.
    private static string FileKindsText { get; } = string.Concat(
        AttachmentContentTypes.AllowedKindsText[..1].ToUpperInvariant(),
        AttachmentContentTypes.AllowedKindsText[1..]);

    private string FileLimitsText =>
        $"Maks {ByteSize.Format(Options.MaxFileBytes)} per fil, opptil {Options.MaxFilesPerSelection} filer om gangen";

    // Choosing a file is a click inside the popover, which popover="auto"
    // doesn't treat as a dismissal, so the menu would stay open over the upload.
    private async Task OnFilesChosen(InputFileChangeEventArgs args)
    {
        try
        {
            await JS.InvokeVoidAsync("chatClient.hidePopover", _popover);
        }
        catch (JSException ex)
        {
            Logger.LogWarning(ex, "ComposerAddMenu could not close its popover after a file selection");
        }

        await OnFilesSelected.InvokeAsync(args);
    }
}
