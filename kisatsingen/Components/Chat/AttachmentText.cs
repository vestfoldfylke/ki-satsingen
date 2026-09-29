using System.Globalization;
using kisatsingen.Services.Attachments;

namespace kisatsingen.Components.Chat;

// Shared by the composer and the files panel, so an attachment reads the same
// in both.
internal static class AttachmentText
{
    private const double Kilobyte = 1024;
    private const double Megabyte = Kilobyte * 1024;

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

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / Kilobyte:0} kB"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{bytes / Megabyte:0.#} MB")
    };

    // From the declared size, which is only a display estimate here.
    private static int Percent(PendingAttachment attachment) =>
        attachment.DeclaredSizeBytes <= 0
            ? 0
            : (int)Math.Min(100, attachment.ReceivedBytes * 100 / attachment.DeclaredSizeBytes);
}
