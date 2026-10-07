namespace kisatsingen.Services.Attachments;

public enum AttachmentStatus
{
    // Reserved, waiting for the files before it in the selection.
    Waiting,
    Uploading,
    Ready,

    // Kept so the page can say why, until the user dismisses it.
    Rejected
}
