namespace kisatsingen.Services.Attachments;

// The per-user limits, as pure decisions over the registry's entries. The
// registry calls these under its lock and acts on the answers; nothing here
// changes an entry.
internal sealed class PendingAttachmentLimits(AttachmentOptions options)
{
    // Enough to explain one whole selection's refusals.
    private const int MaxRejectedPerScope = 10;

    // Null when the file may be reserved.
    public string? FindReservationRefusal(IEnumerable<PendingAttachmentEntry> entries, string ownerId, long declaredSizeBytes)
    {
        // The declared size is the client's claim: good enough to refuse an
        // honest oversized file early, never trusted to reserve one.
        if (declaredSizeBytes > options.MaxFileBytes)
        {
            return UploadRejections.TooLarge(options.MaxFileBytes);
        }

        var ownersEntries = entries.Where(entry => entry.OwnerId == ownerId && entry.Status != AttachmentStatus.Rejected).ToList();
        if (ownersEntries.Count >= options.MaxPendingFilesPerUser)
        {
            return UploadRejections.TooManyPending(options.MaxPendingFilesPerUser);
        }

        if (PendingBytes(ownersEntries) + declaredSizeBytes > options.MaxPendingBytesPerUser)
        {
            return UploadRejections.TooManyPendingBytes(options.MaxPendingBytesPerUser);
        }

        return null;
    }

    // Checked per file as it starts rather than at reservation, because the
    // other uploads that count may be in another tab.
    public bool CanStartUpload(IEnumerable<PendingAttachmentEntry> entries, string ownerId) =>
        entries.Count(entry => entry.OwnerId == ownerId && entry.Status == AttachmentStatus.Uploading) < options.MaxConcurrentUploadsPerUser;

    // Null when the upload that just arrived may be kept. The reservation check
    // trusted the declared size; this one takes the real one.
    public string? FindCompletionRefusal(IEnumerable<PendingAttachmentEntry> entries, string ownerId, Guid arrivingUploadId, long arrivingSizeBytes)
    {
        var others = entries.Where(entry => entry.OwnerId == ownerId && entry.UploadId != arrivingUploadId && entry.Status != AttachmentStatus.Rejected);
        return PendingBytes(others) + arrivingSizeBytes > options.MaxPendingBytesPerUser
            ? UploadRejections.TooManyPendingBytes(options.MaxPendingBytesPerUser)
            : null;
    }

    // Refused entries count towards no limit, so they are capped per scope:
    // otherwise picking unsupported files over and over would grow memory
    // without bound, and slow every scan under the lock for every user. The
    // oldest go first.
    public List<PendingAttachmentEntry> SelectEvictedRejections(IEnumerable<PendingAttachmentEntry> entries, PendingAttachmentEntry justRejected)
    {
        var rejectedInScope = entries
            .Where(entry => entry.OwnerId == justRejected.OwnerId && entry.ScopeKey == justRejected.ScopeKey && entry.Status == AttachmentStatus.Rejected)
            .OrderBy(entry => entry.CreatedAt)
            .ToList();

        return rejectedInScope.Take(Math.Max(0, rejectedInScope.Count - MaxRejectedPerScope)).ToList();
    }

    // Stored files count at their real size; the rest at what they claim,
    // capped at the file limit the upload will enforce.
    private long PendingBytes(IEnumerable<PendingAttachmentEntry> entries) =>
        entries.Sum(entry => entry.SizeBytes ?? Math.Min(entry.DeclaredSizeBytes, options.MaxFileBytes));
}
