namespace kisatsingen.Services.Attachments;

// An upload the quota accepted, ready to be sent. Its file belongs to the
// composer until send, then to the SentAttachments it was sent in.
public sealed record ReadyAttachment(
    Guid AttachmentId,
    string FileName,
    AttachmentType Type,
    long SizeBytes,
    string Sha256,
    TempFile File);
