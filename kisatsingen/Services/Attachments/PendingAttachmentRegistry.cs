namespace kisatsingen.Services.Attachments;

// Files attached but not yet sent, in memory, for every user. A scope key
// names the place they were attached (one chat view's composer, per tab), so
// two tabs never see each other's files. Every lookup also takes the owner, so
// a key alone reaches nothing.
//
// The per-user limits are checked here, before any bytes are read, and again
// against the real size once they have been.
//
// Per instance: a restart loses pending files (their temp files are deleted at
// startup), and with scale-out a circuit only sees its own instance's files.
public sealed class PendingAttachmentRegistry
{
    private readonly Lock _entriesLock = new();
    private readonly Dictionary<Guid, PendingAttachmentEntry> _entries = [];
    private readonly TempFileStore _store;
    private readonly PendingAttachmentLimits _limits;
    private readonly TimeProvider _time;

    public PendingAttachmentRegistry(TempFileStore store, AttachmentOptions options, TimeProvider time)
    {
        _store = store;
        _limits = new PendingAttachmentLimits(options);
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
            var refusal = type is null
                ? UploadRejections.UnsupportedType
                : _limits.FindReservationRefusal(_entries.Values, ownerId, declaredSizeBytes);

            var entry = new PendingAttachmentEntry(Guid.NewGuid(), ownerId, scopeKey, fileName, type, declaredSizeBytes, _time.GetUtcNow());
            _entries.Add(entry.UploadId, entry);

            if (refusal is not null)
            {
                evicted = RejectLocked(entry, refusal);
                reservation = null;
            }
            else
            {
                reservation = new UploadReservation(entry.UploadId, type!, entry.Cancellation.Token);
            }
        }

        Discard(evicted);
        Changed?.Invoke(scopeKey);
        return reservation;
    }

    // Checked per file as it starts rather than at reservation, because the
    // other uploads that count may be in another tab.
    public bool TryStartUpload(Guid uploadId)
    {
        bool isStarted;
        Guid scopeKey;
        List<PendingAttachmentEntry> evicted = [];
        lock (_entriesLock)
        {
            if (!_entries.TryGetValue(uploadId, out var entry) || entry.Status != AttachmentStatus.Waiting)
            {
                return false;
            }

            isStarted = _limits.CanStartUpload(_entries.Values, entry.OwnerId);
            if (isStarted)
            {
                entry.Status = AttachmentStatus.Uploading;
            }
            else
            {
                evicted = RejectLocked(entry, UploadRejections.ConcurrentUploads);
            }

            scopeKey = entry.ScopeKey;
        }

        Discard(evicted);
        Changed?.Invoke(scopeKey);
        return isStarted;
    }

    public void ReportProgress(Guid uploadId, long receivedBytes)
    {
        Guid scopeKey;
        lock (_entriesLock)
        {
            if (!_entries.TryGetValue(uploadId, out var entry) || !entry.AdvanceProgress(receivedBytes))
            {
                return;
            }

            scopeKey = entry.ScopeKey;
        }

        Changed?.Invoke(scopeKey);
    }

    // Takes the file in every case, so a caller never has to decide whether to
    // delete it: kept when the attachment is still there and within the
    // limits, deleted when it was removed mid-upload or is over them.
    public void Complete(Guid uploadId, UploadOutcome.Stored stored)
    {
        Guid? scopeKey = null;
        var isKept = false;
        List<PendingAttachmentEntry> evicted = [];
        lock (_entriesLock)
        {
            if (_entries.TryGetValue(uploadId, out var entry))
            {
                if (_limits.FindCompletionRefusal(_entries.Values, entry.OwnerId, uploadId, stored.SizeBytes) is { } refusal)
                {
                    evicted = RejectLocked(entry, refusal);
                }
                else
                {
                    entry.Complete(stored);
                    isKept = true;
                }

                scopeKey = entry.ScopeKey;
            }
        }

        if (!isKept)
        {
            _store.Delete(stored.File);
        }

        Discard(evicted);
        if (scopeKey is Guid changed)
        {
            Changed?.Invoke(changed);
        }
    }

    public void Reject(Guid uploadId, string reason)
    {
        Guid scopeKey;
        List<PendingAttachmentEntry> evicted;
        lock (_entriesLock)
        {
            if (!_entries.TryGetValue(uploadId, out var entry))
            {
                return;
            }

            evicted = RejectLocked(entry, reason);
            scopeKey = entry.ScopeKey;
        }

        Discard(evicted);
        Changed?.Invoke(scopeKey);
    }

    // The one way an entry is refused, so the cap on refused entries (see
    // PendingAttachmentLimits) holds on every path. Returns the entries it
    // pushed out, for the caller to discard once outside the lock.
    private List<PendingAttachmentEntry> RejectLocked(PendingAttachmentEntry entry, string reason)
    {
        entry.Reject(reason);

        var evicted = _limits.SelectEvictedRejections(_entries.Values, entry);
        foreach (var old in evicted)
        {
            _entries.Remove(old.UploadId);
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

    // Hands the ready attachments among uploadIds over to processing; the
    // caller names them, so one that becomes ready later is not taken. They
    // leave the registry without their temp files being deleted: the caller
    // now owns them (see ReadyAttachment). Refused and unfinished ones stay.
    public IReadOnlyList<ReadyAttachment> TakeReady(string ownerId, Guid scopeKey, IReadOnlySet<Guid> uploadIds)
    {
        List<ReadyAttachment> taken = [];
        lock (_entriesLock)
        {
            var requested = _entries.Values
                .Where(entry => entry.OwnerId == ownerId && entry.ScopeKey == scopeKey && uploadIds.Contains(entry.UploadId))
                .OrderBy(entry => entry.CreatedAt)
                .ToList();

            foreach (var entry in requested)
            {
                if (entry.ToReady() is { } handedOver)
                {
                    _entries.Remove(entry.UploadId);
                    taken.Add(handedOver);
                }
            }
        }

        // Outside the lock, like all file I/O here. Without it, a file that
        // waited nearly a day before being sent could be swept mid-processing.
        // Between leaving the registry and having its age reset, a file just short of
        // the maximum age is exposed for microseconds to a sweep that runs at
        // that exact moment; accepted rather than doing file I/O under the lock.
        foreach (var attachment in taken)
        {
            _store.ResetAge(attachment.File);
        }

        if (taken.Count > 0)
        {
            Changed?.Invoke(scopeKey);
        }

        return taken;
    }

    // Also cancels its upload, if it is still uploading.
    public bool Remove(string ownerId, Guid uploadId)
    {
        PendingAttachmentEntry? removed;
        lock (_entriesLock)
        {
            if (!_entries.TryGetValue(uploadId, out removed) || removed.OwnerId != ownerId)
            {
                return false;
            }

            _entries.Remove(uploadId);
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
                _entries.Remove(entry.UploadId);
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
    private void Discard(IEnumerable<PendingAttachmentEntry> entries)
    {
        foreach (var entry in entries)
        {
            entry.Cancellation.Cancel();
            if (entry.File is not null)
            {
                _store.Delete(entry.File);
            }
        }
    }
}
