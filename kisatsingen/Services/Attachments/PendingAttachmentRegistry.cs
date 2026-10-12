namespace kisatsingen.Services.Attachments;

// Files attached but not yet sent, in memory, for every user. A scope key
// names the place they were attached (one chat view's composer, per tab), so
// two tabs never see each other's files. Every lookup also takes the owner, so
// a key alone reaches nothing.
//
// Whether a file is acceptable at all (its type, its declared size) is decided
// here; whether there is room for it is AttachmentQuota's.
//
// Per instance: a restart loses pending files (their temp files are deleted at
// startup), and with scale-out a circuit only sees its own instance's files.
public sealed class PendingAttachmentRegistry
{
    private readonly Lock _entriesLock = new();
    private readonly Dictionary<Guid, PendingAttachmentEntry> _entries = [];
    // Enough to explain one whole selection's refusals.
    private const int MaxRejectedPerScope = 10;

    private readonly AttachmentStorage _storage;
    private readonly AttachmentQuota _quota;
    private readonly AttachmentOptions _options;
    private readonly TimeProvider _time;

    public PendingAttachmentRegistry(AttachmentStorage storage, AttachmentQuota quota, AttachmentOptions options, TimeProvider time)
    {
        _storage = storage;
        _quota = quota;
        _options = options;
        _time = time;
    }

    // Raised outside the lock, with the scope key whose files changed.
    public event Action<Guid>? Changed;

    // Null when refused; the refusal is then listed with its reason.
    public UploadReservation? Reserve(string ownerId, Guid scopeKey, string fileName, long declaredSizeBytes)
    {
        UploadReservation? reservation;
        List<PendingAttachmentEntry> evicted = [];
        lock (_entriesLock)
        {
            var type = AttachmentContentTypes.FromFileName(fileName);
            var refusal = FindUnacceptableReason(type, declaredSizeBytes);

            // One per picked file; a selection of several files has no ID of
            // its own.
            var attachmentId = Guid.NewGuid();
            var entry = new PendingAttachmentEntry(attachmentId, ownerId, scopeKey, fileName, type, declaredSizeBytes, _time.GetUtcNow());
            _entries.Add(entry.AttachmentId, entry);

            if (refusal is not null)
            {
                evicted = RejectLocked(entry, refusal);
                reservation = null;
            }
            else
            {
                reservation = new UploadReservation(entry.AttachmentId, type!, entry.Cancellation.Token);
            }
        }

        Discard(evicted);
        Changed?.Invoke(scopeKey);
        return reservation;
    }

    // Checked against the quota as the upload starts rather than at
    // reservation: uploads in a tab run one after another, so by now the files
    // before it are on disk at their real size.
    public bool TryStartUpload(Guid attachmentId)
    {
        bool isStarted;
        Guid scopeKey;
        List<PendingAttachmentEntry> evicted = [];
        lock (_entriesLock)
        {
            if (!_entries.TryGetValue(attachmentId, out var entry) || entry.Status != AttachmentStatus.Waiting)
            {
                return false;
            }

            var declaredSizeCheck = _quota.CheckDeclaredSizeFits(entry.OwnerId, entry.DeclaredSizeBytes);
            isStarted = declaredSizeCheck is QuotaCheck.Fits;
            if (declaredSizeCheck is QuotaCheck.Rejected rejected)
            {
                evicted = RejectLocked(entry, rejected.Reason);
            }
            else
            {
                entry.Status = AttachmentStatus.Uploading;
            }

            scopeKey = entry.ScopeKey;
        }

        Discard(evicted);
        Changed?.Invoke(scopeKey);
        return isStarted;
    }

    public void ReportProgress(Guid attachmentId, long receivedBytes)
    {
        Guid scopeKey;
        lock (_entriesLock)
        {
            if (!_entries.TryGetValue(attachmentId, out var entry) || !entry.AdvanceProgress(receivedBytes))
            {
                return;
            }

            scopeKey = entry.ScopeKey;
        }

        Changed?.Invoke(scopeKey);
    }

    // Takes the file in every case, so a caller never has to decide whether to
    // delete it: marked ready when the attachment is still uploading, is no duplicate
    // and fits the quota; deleted otherwise. A file the quota refuses is
    // already deleted by the quota.
    public void Complete(Guid attachmentId, UploadOutcome.Stored stored)
    {
        Guid? scopeKey = null;
        var isFileHandled = false;
        List<PendingAttachmentEntry> evicted = [];
        lock (_entriesLock)
        {
            if (_entries.TryGetValue(attachmentId, out var entry) && entry.Status == AttachmentStatus.Uploading)
            {
                if (PendingDuplicates.IsAlreadyPending(_entries.Values, entry.OwnerId, entry.ScopeKey, stored.Sha256))
                {
                    evicted = RejectLocked(entry, UploadRejections.AlreadyPending);
                }
                else if (_quota.MarkReadyIfFits(entry.OwnerId, stored) is QuotaCheck.Rejected rejected)
                {
                    evicted = RejectLocked(entry, rejected.Reason);
                    isFileHandled = true;
                }
                else
                {
                    entry.Complete(stored);
                    isFileHandled = true;
                }

                scopeKey = entry.ScopeKey;
            }
        }

        if (!isFileHandled)
        {
            _storage.Delete(stored.File);
        }

        Discard(evicted);
        if (scopeKey is Guid changed)
        {
            Changed?.Invoke(changed);
        }
    }

    public void Reject(Guid attachmentId, string reason)
    {
        Guid scopeKey;
        List<PendingAttachmentEntry> evicted;
        lock (_entriesLock)
        {
            if (!_entries.TryGetValue(attachmentId, out var entry))
            {
                return;
            }

            evicted = RejectLocked(entry, reason);
            scopeKey = entry.ScopeKey;
        }

        Discard(evicted);
        Changed?.Invoke(scopeKey);
    }

    // Null when acceptable. The declared size is the browser's claim: good
    // enough to refuse an honest oversized file before any bytes are read,
    // never trusted to count it.
    private string? FindUnacceptableReason(AttachmentType? type, long declaredSizeBytes) =>
        type is null ? UploadRejections.UnsupportedType
        : declaredSizeBytes < 0 ? UploadRejections.UnreadableSize
        : declaredSizeBytes > _options.MaxFileBytes ? UploadRejections.TooLarge(_options.MaxFileBytes)
        : null;

    // The one way an entry is refused, so the cap on refused entries holds on
    // every path. Refused entries count towards no limit, so without the cap
    // picking unsupported files over and over would grow memory without
    // bound; the oldest go first. Returns the entries it pushed out, for the
    // caller to discard once outside the lock.
    private List<PendingAttachmentEntry> RejectLocked(PendingAttachmentEntry entry, string reason)
    {
        entry.Reject(reason);

        var rejectedInScope = _entries.Values
            .Where(other => other.OwnerId == entry.OwnerId && other.ScopeKey == entry.ScopeKey && other.Status == AttachmentStatus.Rejected)
            .OrderBy(other => other.CreatedAt)
            .ToList();
        var evicted = rejectedInScope.Take(Math.Max(0, rejectedInScope.Count - MaxRejectedPerScope)).ToList();
        foreach (var old in evicted)
        {
            _entries.Remove(old.AttachmentId);
        }

        return evicted;
    }

    public IReadOnlyList<PendingAttachment> List(string ownerId, Guid scopeKey)
    {
        lock (_entriesLock)
        {
            return _entries.Values
                .Where(entry => entry.OwnerId == ownerId && entry.ScopeKey == scopeKey)
                .OrderBy(entry => entry.CreatedAt)
                .Select(entry => entry.ToView())
                .ToList();
        }
    }

    // Hands the ready attachments among attachmentIds over to processing; the
    // caller names them, so one that becomes ready later is not taken. They
    // leave the registry without their temp files being deleted: the caller
    // now owns them (see ReadyAttachment). Refused and unfinished ones stay.
    public IReadOnlyList<ReadyAttachment> TakeReady(string ownerId, Guid scopeKey, IReadOnlySet<Guid> attachmentIds)
    {
        List<ReadyAttachment> taken = [];
        lock (_entriesLock)
        {
            var requested = _entries.Values
                .Where(entry => entry.OwnerId == ownerId && entry.ScopeKey == scopeKey && attachmentIds.Contains(entry.AttachmentId))
                .OrderBy(entry => entry.CreatedAt)
                .ToList();

            foreach (var entry in requested)
            {
                if (entry.ToReady() is { } handedOver)
                {
                    _entries.Remove(entry.AttachmentId);
                    taken.Add(handedOver);
                }
            }
        }

        if (taken.Count > 0)
        {
            Changed?.Invoke(scopeKey);
        }

        return taken;
    }

    // Also cancels its upload, if it is still uploading.
    public bool Remove(string ownerId, Guid attachmentId)
    {
        PendingAttachmentEntry? removed;
        lock (_entriesLock)
        {
            if (!_entries.TryGetValue(attachmentId, out removed) || removed.OwnerId != ownerId)
            {
                return false;
            }

            _entries.Remove(attachmentId);
        }

        Discard([removed]);
        Changed?.Invoke(removed.ScopeKey);
        return true;
    }

    public void RemoveAll(string ownerId, Guid scopeKey) =>
        RemoveWhere(entry => entry.OwnerId == ownerId && entry.ScopeKey == scopeKey);

    // For the sweep: a file attached a day ago and never sent is abandoned.
    public int RemoveOlderThan(DateTimeOffset cutoff) =>
        RemoveWhere(entry => entry.CreatedAt < cutoff);

    private int RemoveWhere(Func<PendingAttachmentEntry, bool> predicate)
    {
        List<PendingAttachmentEntry> removed;
        lock (_entriesLock)
        {
            removed = _entries.Values.Where(predicate).ToList();
            foreach (var entry in removed)
            {
                _entries.Remove(entry.AttachmentId);
            }
        }

        Discard(removed);
        foreach (var scopeKey in removed.Select(entry => entry.ScopeKey).Distinct())
        {
            Changed?.Invoke(scopeKey);
        }

        return removed.Count;
    }

    // Outside the lock: file I/O must not hold up every other user's uploads.
    // Cancelled, never disposed: the upload may still be linking to its token.
    // Deleting a file is all it takes to free its owner's quota.
    private void Discard(IEnumerable<PendingAttachmentEntry> entries)
    {
        foreach (var entry in entries)
        {
            entry.Cancellation.Cancel();
            if (entry.File is not null)
            {
                _storage.Delete(entry.File);
            }
        }
    }
}
