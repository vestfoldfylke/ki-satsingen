using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Chat;

// What the model was told about a turn's attachments.
public sealed partial class AttachmentsBlock : ComponentBase
{
    [Parameter, EditorRequired]
    public required IReadOnlyList<TurnAttachment> Attachments { get; set; }

    private int UnavailableCount => Attachments.Count(attachment => attachment.UnavailableReason is not null);

    private string SummaryText => (UnavailableCount, Attachments.Count) switch
    {
        (0, _) => "Behandlet vedlegg",
        (_, 1) => "Vedlegget kunne ikke behandles",
        var (unavailable, total) => $"{unavailable} av {total} vedlegg kunne ikke behandles"
    };
}
