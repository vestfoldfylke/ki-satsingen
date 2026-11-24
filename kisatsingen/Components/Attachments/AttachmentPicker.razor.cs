using kisatsingen.Services;
using kisatsingen.Services.Attachments;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace kisatsingen.Components.Attachments;

// Selects files to attach, with the allowed types, the limits and an optional
// warning beside the button.
public sealed partial class AttachmentPicker : ComponentBase
{
    // Unique per instance, so two pickers can share a page.
    private readonly string _idPrefix = $"attachment-picker-{Guid.NewGuid():N}";

    [Inject]
    public required AttachmentOptions Options { get; set; }

    [Parameter, EditorRequired]
    public required string Label { get; set; }

    [Parameter]
    public bool IsDisabled { get; set; }

    [Parameter]
    public EventCallback<InputFileChangeEventArgs> OnFilesSelected { get; set; }

    // Shown above the button and read with the input; null shows none.
    [Parameter]
    public string? Warning { get; set; }

    private string WarningId => $"{_idPrefix}-warning";
    private string NameId => $"{_idPrefix}-name";
    private string KindsId => $"{_idPrefix}-kinds";
    private string LimitsId => $"{_idPrefix}-limits";

    private string DescriptionIds => Warning is null
        ? $"{KindsId} {LimitsId}"
        : $"{WarningId} {KindsId} {LimitsId}";

    // The list is written to sit mid-sentence; here it starts the line.
    private static string FileKindsText { get; } = string.Concat(
        AttachmentContentTypes.AllowedKindsText[..1].ToUpperInvariant(),
        AttachmentContentTypes.AllowedKindsText[1..]);

    private string FileLimitsText =>
        $"Maks {ByteSize.Format(Options.MaxFileBytes)} per fil, opptil {AttachmentOptions.MaxFilesPerSelection} filer om gangen";
}
