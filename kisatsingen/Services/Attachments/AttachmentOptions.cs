namespace kisatsingen.Services.Attachments;

// Defaults in code, overridable per environment under this section name. The
// temp folder is deliberately not among them: see TempFileStore.
public sealed class AttachmentOptions
{
    public const string SectionName = "Attachments";

    // High on purpose until uploads over SignalR are measured in the cloud.
    public long MaxFileBytes { get; init; } = 50 * ByteSize.Megabyte;

    // GetMultipleFiles throws above its limit, so a selection is checked
    // against this before any file is read.
    public int MaxFilesPerSelection { get; init; } = 10;

    // Across all of a user's tabs, counting files still uploading.
    public int MaxPendingFilesPerUser { get; init; } = 10;

    // Pending files sit on disk until their message is sent.
    public long MaxPendingBytesPerUser { get; init; } = 200 * ByteSize.Megabyte;

    // Uploads share the circuit's SignalR connection with rendering.
    public int MaxConcurrentUploadsPerUser { get; init; } = 2;

    // Catches whatever the other cleanup paths miss.
    public TimeSpan TempFileMaxAge { get; init; } = TimeSpan.FromHours(24);

    public TimeSpan TempFileSweepInterval { get; init; } = TimeSpan.FromHours(1);

    public void Validate()
    {
        if (MaxFileBytes <= 0 || MaxFilesPerSelection <= 0 || MaxPendingFilesPerUser <= 0
            || MaxPendingBytesPerUser <= 0 || MaxConcurrentUploadsPerUser <= 0
            || TempFileMaxAge <= TimeSpan.Zero || TempFileSweepInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"Every '{SectionName}' setting must be positive. Check the '{SectionName}' section in configuration.");
        }

        if (MaxPendingBytesPerUser < MaxFileBytes)
        {
            throw new InvalidOperationException(
                $"'{SectionName}:MaxPendingBytesPerUser' ({MaxPendingBytesPerUser}) is below '{SectionName}:MaxFileBytes' ({MaxFileBytes}), so a file at the size limit could never be attached. Raise MaxPendingBytesPerUser or lower MaxFileBytes.");
        }
    }
}
