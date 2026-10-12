using kisatsingen.Services.Attachments;
using Microsoft.AspNetCore.Components.Forms;

namespace kisatsingen.Services.Chat;

// The open chat view's attachments, for one tab: picked in the composer,
// uploaded to temp files, and owned here until they are sent or discarded.
//
// No lock: it is only ever used from its circuit's dispatcher, which runs one
// call at a time. Blazor runs UI events, the awaits that resume from them, and
// disposal there.
public sealed class ChatAttachments(
    IAuthenticationService authenticationService,
    AttachmentUploader uploader,
    AttachmentQuota quota,
    AttachmentOptions options,
    TimeProvider time,
    ILogger<ChatAttachments> logger) : IDisposable
{
    private readonly List<PendingAttachmentEntry> _entries = [];

    private string? _ownerId;

    public event Action? Changed;

    // About the selection as a whole, which no single attachment can carry.
    public string? SelectionNotice { get; private set; }

    public IReadOnlyList<PendingAttachment> Pending => [.. _entries.Select(entry => entry.ToView())];

    // Holds send, and the picker: a new selection makes the files of the one
    // still uploading unreadable.
    public bool IsUploading =>
        _entries.Any(entry => entry.Status is AttachmentStatus.Waiting or AttachmentStatus.Uploading);

    public async Task AttachAsync(InputFileChangeEventArgs selection)
    {
        var ownerId = await EnsureOwnerAsync();

        // Checked before GetMultipleFiles, which throws above its limit.
        if (selection.FileCount > options.MaxFilesPerSelection)
        {
            SelectionNotice = UploadRejections.TooManyInSelection(options.MaxFilesPerSelection);
            Changed?.Invoke();
            return;
        }

        SelectionNotice = null;
        DiscardRejected();

        // All listed up front, so the whole selection shows at once and a file
        // that is not acceptable is refused before any bytes move.
        var waiting = new List<(PendingAttachmentEntry Entry, IBrowserFile File, AttachmentType Type)>();
        foreach (var file in selection.GetMultipleFiles(options.MaxFilesPerSelection))
        {
            var entry = Add(file);
            if (entry.Status == AttachmentStatus.Waiting && entry.Type is { } type)
            {
                waiting.Add((entry, file, type));
            }
        }

        Changed?.Invoke();

        // One at a time: they share the circuit's connection, so in parallel
        // they would only take turns on it.
        foreach (var (entry, file, type) in waiting)
        {
            await AttachOneAsync(ownerId, entry, file, type);
        }
    }

    private PendingAttachmentEntry Add(IBrowserFile file)
    {
        var fileName = AttachmentFileName.Normalize(file.Name);
        var type = AttachmentContentTypes.FromFileName(fileName);

        // One per picked file; a selection of several files has no ID of its
        // own.
        var attachmentId = Guid.NewGuid();
        var entry = new PendingAttachmentEntry(attachmentId, fileName, type, file.Size, time.GetUtcNow());
        _entries.Add(entry);

        if (FindUnacceptableReason(type, file.Size) is { } reason)
        {
            entry.Reject(reason);
        }

        return entry;
    }

    // Null when acceptable. The declared size is the browser's claim: good
    // enough to refuse an honest oversized file before any bytes are read,
    // never trusted to count it.
    private string? FindUnacceptableReason(AttachmentType? type, long declaredSizeBytes) =>
        type is null ? UploadRejections.UnsupportedType
        : declaredSizeBytes < 0 ? UploadRejections.UnreadableSize
        : declaredSizeBytes > options.MaxFileBytes ? UploadRejections.TooLarge(options.MaxFileBytes)
        : null;

    // Never throws: this runs in a UI event handler, where an exception would
    // end the circuit.
    private async Task AttachOneAsync(string ownerId, PendingAttachmentEntry entry, IBrowserFile file, AttachmentType type)
    {
        // Removed, or the view moved on, while it waited for its turn.
        if (!_entries.Contains(entry))
        {
            return;
        }

        // Checked as the upload starts rather than when picked: uploads in a
        // tab run one after another, so by now the files before it are on disk
        // at their real size.
        if (quota.CheckDeclaredSizeFits(ownerId, entry.DeclaredSizeBytes) is QuotaCheck.Rejected refused)
        {
            Reject(entry, refused.Reason);
            return;
        }

        entry.Status = AttachmentStatus.Uploading;
        Changed?.Invoke();

        var cancellation = entry.Cancellation.Token;
        var progress = new ImmediateProgress(receivedBytes =>
        {
            if (entry.AdvanceProgress(receivedBytes))
            {
                Changed?.Invoke();
            }
        });

        try
        {
            await using var uploadStream = file.OpenReadStream(options.MaxFileBytes, cancellation);
            var outcome = await uploader.UploadAsync(uploadStream, ownerId, entry.AttachmentId, type, options.MaxFileBytes, progress, cancellation);

            switch (outcome)
            {
                case UploadOutcome.Stored stored:
                    Complete(ownerId, entry, stored);
                    break;
                case UploadOutcome.Rejected rejected:
                    Reject(entry, rejected.Reason);
                    break;
            }
        }
        // Removed, or the view moved on: the entry is already gone.
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        // A lost connection mid-upload surfaces as whatever the stream throws.
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Uploading attachment {AttachmentId} failed after it started.", entry.AttachmentId);
            Reject(entry, UploadRejections.Interrupted);
        }
    }

    // Takes the file in every case: marked ready when the entry is still here,
    // is no duplicate and fits the quota; deleted otherwise. A file the quota
    // refuses is already deleted by the quota.
    private void Complete(string ownerId, PendingAttachmentEntry entry, UploadOutcome.Stored stored)
    {
        if (!_entries.Contains(entry))
        {
            stored.File.Dispose();
            return;
        }

        if (IsAlreadyReady(stored.Sha256))
        {
            stored.File.Dispose();
            Reject(entry, UploadRejections.AlreadyPending);
            return;
        }

        if (quota.MarkReadyIfFits(ownerId, stored) is QuotaCheck.Rejected rejected)
        {
            Reject(entry, rejected.Reason);
            return;
        }

        entry.Complete(stored);
        Changed?.Invoke();
    }

    // Uploads in a tab run one after another, so every earlier copy of the
    // same content is already ready when this one is checked.
    private bool IsAlreadyReady(string sha256) =>
        _entries.Any(entry => entry.Status == AttachmentStatus.Ready && entry.Sha256 == sha256);

    private void Reject(PendingAttachmentEntry entry, string reason)
    {
        entry.Reject(reason);
        Changed?.Invoke();
    }

    public void Remove(Guid attachmentId)
    {
        if (_entries.Find(entry => entry.AttachmentId == attachmentId) is { } entry)
        {
            Discard(entry);
            Changed?.Invoke();
        }
    }

    // On send: the attachments ready now are this message's, and one that
    // finishes uploading later stays for the next.
    internal MessageAttachments AttachReadyToMessage()
    {
        var readyAttachmentIds = _entries
            .Where(entry => entry.Status == AttachmentStatus.Ready)
            .Select(entry => entry.AttachmentId)
            .ToHashSet();

        DiscardRejected();
        Changed?.Invoke();
        return new MessageAttachments(this, readyAttachmentIds);
    }

    // Removes the ready attachments among attachmentIds and returns them; one
    // that became ready later is not taken. Their files are not deleted:
    // whoever takes them now owns them (see ReadyAttachment).
    internal IReadOnlyList<ReadyAttachment> TakeReady(IReadOnlySet<Guid> attachmentIds)
    {
        var taken = new List<ReadyAttachment>();
        foreach (var entry in _entries.Where(entry => attachmentIds.Contains(entry.AttachmentId)).ToList())
        {
            if (entry.ToReady() is { } ready)
            {
                _entries.Remove(entry);
                taken.Add(ready);
            }
        }

        if (taken.Count > 0)
        {
            Changed?.Invoke();
        }

        return taken;
    }

    // For a view switch: what was attached here is not the next chat's.
    public void Reset()
    {
        DeleteAll();
        SelectionNotice = null;
        Changed?.Invoke();
    }

    // The circuit is gone, so nothing can send what is pending.
    public void Dispose() => DeleteAll();

    private void DeleteAll()
    {
        foreach (var entry in _entries.ToList())
        {
            Discard(entry);
        }
    }

    // Refused entries have already said why; a new selection or a send clears
    // them, so they never pile up.
    private void DiscardRejected()
    {
        foreach (var entry in _entries.Where(entry => entry.Status == AttachmentStatus.Rejected).ToList())
        {
            Discard(entry);
        }
    }

    // The one way an entry leaves without being sent: its upload stops and
    // its file, if stored, is deleted. Cancelled, never disposed: the upload
    // may still be linking to its token.
    private void Discard(PendingAttachmentEntry entry)
    {
        _entries.Remove(entry);
        entry.Cancellation.Cancel();
        entry.File?.Dispose();
    }

    private async Task<string> EnsureOwnerAsync() =>
        _ownerId ??= await authenticationService.RequireUserObjectIdentifierAsync();

    // Progress<T> posts each report to the thread pool, so reports could land
    // out of order.
    private sealed class ImmediateProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
