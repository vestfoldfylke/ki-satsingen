namespace kisatsingen.Services.Attachments;

// One picked file in the composer, from picking until it is sent or discarded.
public sealed class ComposerAttachment(string fileName, long declaredSizeBytes)
{
    // Often enough to look live, rarely enough not to re-render per buffer.
    private const int ProgressStepPercent = 5;

    public Guid AttachmentId { get; } = Guid.NewGuid();
    public string FileName { get; } = fileName;
    public long DeclaredSizeBytes { get; } = declaredSizeBytes;
    public AttachmentStatus Status { get; private set; } = AttachmentStatus.Waiting;
    public int UploadedPercent { get; private set; }
    public string? RejectionReason { get; private set; }

    internal CancellationTokenSource Cancellation { get; } = new();

    // What send hands over; set when the upload is accepted.
    internal ReadyAttachment? Ready { get; private set; }

    internal void StartUploading() => Status = AttachmentStatus.Uploading;

    // True when the percent moved a whole step, so a render is worth it.
    internal bool ReportProgress(long receivedBytes)
    {
        if (DeclaredSizeBytes <= 0)
        {
            return false;
        }

        var percent = (int)Math.Min(100, receivedBytes * 100 / DeclaredSizeBytes);
        if (percent - UploadedPercent < ProgressStepPercent)
        {
            return false;
        }

        UploadedPercent = percent;
        return true;
    }

    internal void MarkReady(AttachmentType type, UploadOutcome.Stored stored)
    {
        Status = AttachmentStatus.Ready;
        Ready = new ReadyAttachment(AttachmentId, FileName, type, stored.SizeBytes, stored.Sha256, stored.File);
    }

    internal void Reject(string reason)
    {
        Status = AttachmentStatus.Rejected;
        RejectionReason = reason;
    }
}
