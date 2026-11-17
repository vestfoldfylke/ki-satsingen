using kisatsingen.Services;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Attachments;

// One file being attached or added. Knows nothing about uploads: each caller
// turns its own status into an AttachmentCardStatus.
public sealed partial class AttachmentCard : ComponentBase
{
    [Parameter, EditorRequired]
    public required string FileName { get; set; }

    [Parameter, EditorRequired]
    public required long SizeBytes { get; set; }

    // Null when the file is there: the card then shows only its type and size.
    [Parameter]
    public AttachmentCardStatus? Status { get; set; }

    // The remove button is drawn only when this is set.
    [Parameter]
    public EventCallback OnRemove { get; set; }

    private string TypeAndSize =>
        $"{Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant()} · {ByteSize.Format(SizeBytes)}";

    private string? TagText => Status switch
    {
        AttachmentCardStatus.Waiting => "Venter",
        AttachmentCardStatus.Uploading uploading => $"Laster opp {uploading.Percent} %",
        AttachmentCardStatus.Processing => "Behandles …",
        _ => null
    };

    // Stronger once bytes are moving.
    private string TagColor => Status is AttachmentCardStatus.Waiting ? "neutral" : "info";
}
