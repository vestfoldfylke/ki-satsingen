namespace kisatsingen.Services.Attachments;

// The same content picked twice for one scope. Only Ready entries have a
// hash, so this catches every duplicate only when the caller uploads one file
// at a time per scope, as ChatAttachments does. Two copies uploading in
// parallel both pass here; the unique index refuses the second at save.
internal static class PendingDuplicates
{
    public static bool IsAlreadyPending(IEnumerable<PendingAttachmentEntry> entries, string ownerId, Guid scopeKey, string sha256) =>
        entries.Any(entry =>
            entry.OwnerId == ownerId
            && entry.ScopeKey == scopeKey
            && entry.Status == AttachmentStatus.Ready
            && entry.Sha256 == sha256);
}
