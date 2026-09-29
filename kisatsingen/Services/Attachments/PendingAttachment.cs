namespace kisatsingen.Services.Attachments;

// What the page sees. The temp file stays inside the registry, so nothing can
// hold on to a path past its removal.
public sealed record PendingAttachment(
    Guid UploadId,
    string FileName,
    AttachmentStatus Status,
    long DeclaredSizeBytes,
    long ReceivedBytes,
    string? RejectionReason,
    DateTimeOffset CreatedAt);
