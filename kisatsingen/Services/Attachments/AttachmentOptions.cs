namespace kisatsingen.Services.Attachments;

// Defaults in code, overridable per environment under this section name. The
// temp folder is deliberately not among them: see AttachmentStorage.
public sealed class AttachmentOptions
{
    public const string SectionName = "Attachments";

    // Measured in the cloud: 50 MiB works over SignalR, though slowly.
    public long MaxFileBytes { get; init; } = 50 * ByteSize.Megabyte;

    // GetMultipleFiles throws above its limit, so a selection is checked
    // against this before any file is read.
    public int MaxFilesPerSelection { get; init; } = 10;

    // Across all of a user's tabs, counting files still uploading.
    public int MaxPendingFilesPerUser { get; init; } = 10;

    // A file counts until it is deleted, including while it is processed
    // after its message is sent.
    public long MaxPendingBytesPerUser { get; init; } = 200 * ByteSize.Megabyte;

    // For all users on one instance together: each App Service instance has
    // its own /tmp, which shares the host disk with every app in the plan.
    public long MaxPendingBytesPerInstance { get; init; } = 2000 * ByteSize.Megabyte;

    // How long an unsent attachment may stay pending before the sweep removes
    // it. Temp files nothing tracks are left to the wipe at the next start.
    public TimeSpan TempFileMaxAge { get; init; } = TimeSpan.FromHours(24);

    public TimeSpan TempFileSweepInterval { get; init; } = TimeSpan.FromHours(1);

    public void Validate()
    {
        if (MaxFileBytes <= 0 || MaxFilesPerSelection <= 0 || MaxPendingFilesPerUser <= 0
            || MaxPendingBytesPerUser <= 0 || MaxPendingBytesPerInstance <= 0
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

        if (MaxPendingBytesPerInstance < MaxPendingBytesPerUser)
        {
            throw new InvalidOperationException(
                $"'{SectionName}:MaxPendingBytesPerInstance' ({MaxPendingBytesPerInstance}) is below '{SectionName}:MaxPendingBytesPerUser' ({MaxPendingBytesPerUser}), so one user could never reach their own limit. Raise MaxPendingBytesPerInstance or lower MaxPendingBytesPerUser.");
        }
    }
}
