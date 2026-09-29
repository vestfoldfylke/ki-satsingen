namespace kisatsingen.Services.Attachments;

// Returned by PendingAttachmentRegistry.TakeReady, which forgets it: nothing
// in the registry (a chat switch, the sweep) can delete its temp file any
// more, so whoever holds this must delete File when done with it.
public sealed record ReadyAttachment(
    Guid UploadId,
    string FileName,
    AttachmentType Type,
    long SizeBytes,
    string Sha256,
    TempFile File);
