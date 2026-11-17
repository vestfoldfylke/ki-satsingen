using kisatsingen.Services;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Attachments;

// One file being attached or added. Draws no container, so the caller decides
// whether it sits in a card or a list row. Knows nothing about uploads: each
// caller turns its own status into an AttachmentItemStatus.
public sealed partial class AttachmentItem : ComponentBase
{
    [Parameter, EditorRequired]
    public required string FileName { get; set; }

    [Parameter, EditorRequired]
    public required long SizeBytes { get; set; }

    // Null when the file is there: the item then shows only its type and size.
    [Parameter]
    public AttachmentItemStatus? Status { get; set; }

    // The remove button is drawn only when this is set.
    [Parameter]
    public EventCallback OnRemove { get; set; }

    private string TypeAndSize =>
        $"{Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant()} · {ByteSize.Format(SizeBytes)}";

    private string? TagText => Status switch
    {
        AttachmentItemStatus.Waiting => "Venter",
        AttachmentItemStatus.Uploading uploading => $"Laster opp {uploading.Percent} %",
        AttachmentItemStatus.Processing => "Behandles …",
        _ => null
    };

    // Stronger once bytes are moving.
    private string TagColor => Status is AttachmentItemStatus.Waiting ? "neutral" : "info";
}
