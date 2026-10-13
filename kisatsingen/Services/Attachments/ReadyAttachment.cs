namespace kisatsingen.Services.Attachments;

// An attachment handed over at send. Its file no longer belongs to the
// composer: whoever holds this owns File and disposes it when done.
public sealed record ReadyAttachment(
    Guid AttachmentId,
    string FileName,
    AttachmentType Type,
    long SizeBytes,
    string Sha256,
    TempFile File);
