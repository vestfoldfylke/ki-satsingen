namespace kisatsingen.Services.Attachments;

// The registry's mutable record of one attachment. Only touched under the
// registry's lock; everything outside sees a PendingAttachment snapshot.
internal sealed class PendingAttachmentEntry(
    Guid uploadId,
    string ownerId,
    Guid scopeKey,
    string fileName,
    AttachmentType? type,
    long declaredSizeBytes,
    DateTimeOffset createdAt)
{
    // Often enough to look live, rarely enough not to re-render per buffer.
    private const int ProgressStepPercent = 5;

    private int _reportedPercent;

    public Guid UploadId { get; } = uploadId;
    public string OwnerId { get; } = ownerId;
    public Guid ScopeKey { get; } = scopeKey;
    public string FileName { get; } = fileName;

    // Null only for a file refused for its type.
    public AttachmentType? Type { get; } = type;

    public long DeclaredSizeBytes { get; } = declaredSizeBytes;
    public DateTimeOffset CreatedAt { get; } = createdAt;
    public CancellationTokenSource Cancellation { get; } = new();

    public AttachmentStatus Status { get; set; } = AttachmentStatus.Waiting;
    public long ReceivedBytes { get; private set; }
    public long? SizeBytes { get; private set; }
    public string? Sha256 { get; private set; }
    public TempFile? File { get; private set; }
    public string? RejectionReason { get; private set; }

    // True when the change is big enough to be worth a render.
    public bool AdvanceProgress(long receivedBytes)
    {
        ReceivedBytes = receivedBytes;
        if (DeclaredSizeBytes <= 0)
        {
            return false;
        }

        var percent = (int)Math.Min(100, receivedBytes * 100 / DeclaredSizeBytes);
        if (percent - _reportedPercent < ProgressStepPercent)
        {
            return false;
        }

        _reportedPercent = percent;
        return true;
    }

    public void Complete(UploadOutcome.Stored stored)
    {
        Status = AttachmentStatus.Ready;
        File = stored.File;
        SizeBytes = stored.SizeBytes;
        ReceivedBytes = stored.SizeBytes;
        Sha256 = stored.Sha256;
    }

    public void Reject(string reason)
    {
        Status = AttachmentStatus.Rejected;
        RejectionReason = reason;
    }

    // Null unless ready: only a stored upload has a file to hand over.
    public ReadyAttachment? ToReady() =>
        Status == AttachmentStatus.Ready && Type is not null && File is not null && SizeBytes is { } size && Sha256 is { } sha256
            ? new ReadyAttachment(UploadId, FileName, Type, size, sha256, File)
            : null;

    public PendingAttachment ToView() =>
        new(UploadId, FileName, Status, DeclaredSizeBytes, ReceivedBytes, RejectionReason, CreatedAt);
}
