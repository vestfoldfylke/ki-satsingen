namespace kisatsingen.Services.Attachments;

// What the page sees of one attachment. The temp file stays inside the
// composer's list, so the page can never hold on to a path past its removal.
public sealed record PendingAttachment(
    Guid AttachmentId,
    string FileName,
    AttachmentStatus Status,
    long DeclaredSizeBytes,
    long ReceivedBytes,
    string? RejectionReason,
    DateTimeOffset CreatedAt);
