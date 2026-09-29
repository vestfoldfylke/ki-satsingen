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
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, PendingAttachmentEntry> _entries = [];
    private readonly TempFileStore _store;
    private readonly AttachmentOptions _options;
    private readonly TimeProvider _time;

    public PendingAttachmentRegistry(TempFileStore store, AttachmentOptions options, TimeProvider time)
    {
        _store = store;
        _options = options;
        _time = time;
    }

    // Raised outside the lock, with the scope key whose files changed.
    public event Action<Guid>? Changed;

    // Null when refused; the refusal is then listed with its reason.
    public UploadReservation? Reserve(string ownerId, Guid scopeKey, string fileName, long declaredSizeBytes)
    {
        UploadReservation? reservation;
        lock (_gate)
        {
            var type = AttachmentContentTypes.FromFileName(fileName);
            var refusal = type is null
                ? UploadRejections.UnsupportedType
                : FindReservationRefusal(ownerId, declaredSizeBytes);

            var entry = new PendingAttachmentEntry(Guid.NewGuid(), ownerId, scopeKey, fileName, type, declaredSizeBytes, _time.GetUtcNow());
            if (refusal is not null)
            {
                entry.Reject(refusal);
                reservation = null;
            }
            else
            {
                reservation = new UploadReservation(entry.UploadId, type!, entry.Cancellation.Token);
            }

            _entries.Add(entry.UploadId, entry);
        }

        Changed?.Invoke(scopeKey);
        return reservation;
    }

    // Checked per file as it starts rather than at reservation, because the
    // other uploads that count may be in another tab.
    public bool TryStartUpload(Guid uploadId)
    {
        bool isStarted;
        Guid scopeKey;
        lock (_gate)
        {
            if (!_entries.TryGetValue(uploadId, out var entry) || entry.Status != AttachmentStatus.Waiting)
            {
                return false;
            }

            var uploading = _entries.Values.Count(other => other.OwnerId == entry.OwnerId && other.Status == AttachmentStatus.Uploading);
            isStarted = uploading < _options.MaxConcurrentUploadsPerUser;
            if (isStarted)
            {
                entry.Status = AttachmentStatus.Uploading;
            }
            else
            {
                entry.Reject(UploadRejections.ConcurrentUploads);
            }

            scopeKey = entry.ScopeKey;
        }

        Changed?.Invoke(scopeKey);
        return isStarted;
    }

    public void ReportProgress(Guid uploadId, long receivedBytes)
    {
        Guid scopeKey;
        lock (_gate)
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
        lock (_gate)
        {
            if (_entries.TryGetValue(uploadId, out var entry))
            {
                // The reservation check trusted the declared size; this one does not.
                var otherPendingBytes = PendingBytes(entry.OwnerId, except: uploadId);
                if (otherPendingBytes + stored.SizeBytes > _options.MaxPendingBytesPerUser)
                {
                    entry.Reject(UploadRejections.TooManyPendingBytes(_options.MaxPendingBytesPerUser));
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

        if (scopeKey is Guid changed)
        {
            Changed?.Invoke(changed);
        }
    }

    public void Reject(Guid uploadId, string reason)
    {
        Guid scopeKey;
        lock (_gate)
        {
            if (!_entries.TryGetValue(uploadId, out var entry))
            {
                return;
            }

            entry.Reject(reason);
            scopeKey = entry.ScopeKey;
        }

        Changed?.Invoke(scopeKey);
    }

    public IReadOnlyList<PendingAttachment> List(string ownerId, Guid scopeKey)
    {
        lock (_gate)
        {
            return _entries.Values
                .Where(entry => entry.OwnerId == ownerId && entry.ScopeKey == scopeKey)
                .OrderBy(entry => entry.CreatedAt)
                .Select(entry => entry.ToView())
                .ToList();
        }
    }

    // Hands every ready attachment in the scope over to processing. They leave
    // the registry without their temp files being deleted: the caller now owns
    // them (see ReadyAttachment). Refused and unfinished ones stay.
    public IReadOnlyList<ReadyAttachment> TakeReady(string ownerId, Guid scopeKey)
    {
        List<ReadyAttachment> taken = [];
        lock (_gate)
        {
            var inScope = _entries.Values
                .Where(entry => entry.OwnerId == ownerId && entry.ScopeKey == scopeKey)
                .OrderBy(entry => entry.CreatedAt)
                .ToList();

            foreach (var entry in inScope)
            {
                if (entry.ToReady() is { } handedOver)
                {
                    _entries.Remove(entry.UploadId);
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
    public bool Remove(string ownerId, Guid uploadId)
    {
        PendingAttachmentEntry? removed;
        lock (_gate)
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
        lock (_gate)
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

    private string? FindReservationRefusal(string ownerId, long declaredSizeBytes)
    {
        // The declared size is the client's claim: good enough to refuse an
        // honest oversized file early, never trusted to reserve one.
        if (declaredSizeBytes > _options.MaxFileBytes)
        {
            return UploadRejections.TooLarge(_options.MaxFileBytes);
        }

        var pendingCount = _entries.Values.Count(entry => entry.OwnerId == ownerId && entry.Status != AttachmentStatus.Rejected);
        if (pendingCount >= _options.MaxPendingFilesPerUser)
        {
            return UploadRejections.TooManyPending(_options.MaxPendingFilesPerUser);
        }

        if (PendingBytes(ownerId, except: null) + declaredSizeBytes > _options.MaxPendingBytesPerUser)
        {
            return UploadRejections.TooManyPendingBytes(_options.MaxPendingBytesPerUser);
        }

        return null;
    }

    // Stored files count at their real size; the rest at what they claim,
    // capped at the file limit the upload will enforce.
    private long PendingBytes(string ownerId, Guid? except) =>
        _entries.Values
            .Where(entry => entry.OwnerId == ownerId && entry.UploadId != except && entry.Status != AttachmentStatus.Rejected)
            .Sum(entry => entry.SizeBytes ?? Math.Min(entry.DeclaredSizeBytes, _options.MaxFileBytes));
}
