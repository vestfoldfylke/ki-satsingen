using kisatsingen.Services.Attachments;
using kisatsingen.Services.Chat;

namespace kisatsingen.Components.Chat;

// Shared by the composer, the files panel and the user's bubble, so an
// attachment reads the same in each.
internal static class AttachmentStatusTexts
{
    public static string DescribeStatus(PendingAttachment attachment) => attachment.Status switch
    {
        AttachmentStatus.Waiting => "Venter",
        AttachmentStatus.Uploading => $"Laster opp {Percent(attachment)} %",
        AttachmentStatus.Ready => "Klar",
        AttachmentStatus.Rejected => attachment.RejectionReason ?? "Kunne ikke legges ved",
        _ => attachment.Status.ToString()
    };

    public static bool IsRejected(PendingAttachment attachment) =>
        attachment.Status == AttachmentStatus.Rejected;

    // Null when available: the file's name alone says it is there.
    public static string? DescribeStatus(TurnAttachment attachment) => attachment switch
    {
        { IsProcessing: true } => "Behandles",
        { UnavailableReason: { } reason } => reason,
        _ => null
    };

    public static bool IsUnavailable(TurnAttachment attachment) =>
        attachment.UnavailableReason is not null;

    // From the declared size, which is only a display estimate here.
    private static int Percent(PendingAttachment attachment) =>
        attachment.DeclaredSizeBytes <= 0
            ? 0
            : (int)Math.Min(100, attachment.ReceivedBytes * 100 / attachment.DeclaredSizeBytes);
}
