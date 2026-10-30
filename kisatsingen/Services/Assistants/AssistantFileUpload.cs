namespace kisatsingen.Services.Assistants;

public enum AssistantFileUploadStatus
{
    // Waiting for the files before it in the selection.
    Waiting,
    Uploading,
    Processing,

    // Kept so the page can say why, until the next selection.
    Rejected
}

// One file selected on an assistant's page, from selection until it is saved
// or rejected.
public sealed class AssistantFileUpload(string fileName, long declaredSizeBytes)
{
    // Often enough to look live, rarely enough not to re-render per buffer.
    private const int ProgressStepPercent = 5;

    public Guid Id { get; } = Guid.NewGuid();
    public string FileName { get; } = fileName;
    public long DeclaredSizeBytes { get; } = declaredSizeBytes;
    public AssistantFileUploadStatus Status { get; private set; } = AssistantFileUploadStatus.Waiting;
    public int UploadedPercent { get; private set; }

    // Shown to the user as is.
    public string? RejectionReason { get; private set; }

    // Uploading from the first bytes received, so a file refused before any
    // bytes move never shows as uploading. True when a render is worth it: the
    // upload started, or the percent moved a whole step.
    internal bool ReportProgress(long receivedBytes)
    {
        var isFirstReport = Status == AssistantFileUploadStatus.Waiting;
        Status = AssistantFileUploadStatus.Uploading;

        if (DeclaredSizeBytes <= 0)
        {
            return isFirstReport;
        }

        var percent = (int)Math.Min(100, receivedBytes * 100 / DeclaredSizeBytes);
        if (percent - UploadedPercent < ProgressStepPercent)
        {
            return isFirstReport;
        }

        UploadedPercent = percent;
        return true;
    }

    internal void MarkProcessing() => Status = AssistantFileUploadStatus.Processing;

    internal void MarkRejected(string reason)
    {
        Status = AssistantFileUploadStatus.Rejected;
        RejectionReason = reason;
    }
}
