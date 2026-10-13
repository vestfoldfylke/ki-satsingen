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
    ILogger<ChatAttachments> logger) : IDisposable
{
    private readonly List<ComposerAttachment> _attachments = [];

    private string? _ownerId;

    public event Action? Changed;

    // About the selection as a whole, which no single attachment can carry.
    public string? SelectionNotice { get; private set; }

    public IReadOnlyList<ComposerAttachment> All => _attachments;

    // Holds send, and the picker: a new selection makes the files of the one
    // still uploading unreadable.
    public bool IsUploading =>
        _attachments.Any(attachment => attachment.Status is AttachmentStatus.Waiting or AttachmentStatus.Uploading);

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
        var waiting = new List<(ComposerAttachment Attachment, IBrowserFile File, AttachmentType Type)>();
        foreach (var file in selection.GetMultipleFiles(options.MaxFilesPerSelection))
        {
            var fileName = AttachmentFileName.Normalize(file.Name);
            var type = AttachmentContentTypes.FromFileName(fileName);
            var attachment = new ComposerAttachment(fileName, file.Size);
            _attachments.Add(attachment);

            if (type is null)
            {
                attachment.MarkRejected(UploadRejections.UnsupportedType);
                continue;
            }

            if (FindUnacceptableSizeReason(file.Size) is string reason)
            {
                attachment.MarkRejected(reason);
                continue;
            }

            waiting.Add((attachment, file, type));
        }

        Changed?.Invoke();

        // One at a time: they share the circuit's connection, so in parallel
        // they would only take turns on it.
        foreach (var (attachment, file, type) in waiting)
        {
            await AttachOneAsync(ownerId, attachment, file, type);
            Changed?.Invoke();
        }
    }

    // Null when acceptable. The declared size is the browser's claim: good
    // enough to refuse an honest oversized file before any bytes are read,
    // never trusted to count it.
    private string? FindUnacceptableSizeReason(long declaredSizeBytes) =>
        declaredSizeBytes < 0 ? UploadRejections.UnreadableSize
        : declaredSizeBytes > options.MaxFileBytes ? UploadRejections.TooLarge(options.MaxFileBytes)
        : null;

    // Never throws: this runs in a UI event handler, where an exception would
    // end the circuit. Announces the upload starting and its progress; the
    // outcome is announced by the loop that runs it.
    private async Task AttachOneAsync(string ownerId, ComposerAttachment attachment, IBrowserFile file, AttachmentType type)
    {
        // Removed, or the view moved on, while it waited for its turn.
        if (!_attachments.Contains(attachment))
        {
            return;
        }

        var cancellation = attachment.Cancellation.Token;
        var progress = new ImmediateProgress(receivedBytes =>
        {
            if (attachment.ReportProgress(receivedBytes))
            {
                Changed?.Invoke();
            }
        });

        try
        {
            // Checked as the upload starts rather than when picked: uploads in a
            // tab run one after another, so previously attached files are on disk
            // at their real size.
            if (quota.CheckDeclaredSizeFits(ownerId, attachment.DeclaredSizeBytes) is QuotaCheck.Rejected quotaRejection)
            {
                attachment.MarkRejected(quotaRejection.Reason);
                return;
            }

            attachment.MarkUploading();
            Changed?.Invoke();

            await using var uploadStream = file.OpenReadStream(options.MaxFileBytes, cancellation);
            var outcome = await uploader.UploadAsync(uploadStream, ownerId, attachment.AttachmentId, type, options.MaxFileBytes, progress, cancellation);

            switch (outcome)
            {
                case UploadOutcome.Stored stored:
                    MarkReadyIfAccepted(ownerId, attachment, type, stored);
                    break;
                case UploadOutcome.Rejected rejected:
                    attachment.MarkRejected(rejected.Reason);
                    break;
            }
        }
        // Removed, or the view moved on: the attachment is already gone.
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        // A lost connection mid-upload surfaces as whatever the stream throws,
        // and an attachment folder gone from disk as an I/O error.
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Uploading attachment {AttachmentId} failed.", attachment.AttachmentId);
            attachment.MarkRejected(UploadRejections.Interrupted);
        }
    }

    // Takes the file in every case: marked ready when the attachment is still
    // here, is no duplicate and fits the quota; deleted otherwise. A file the
    // quota refuses is already deleted by the quota.
    private void MarkReadyIfAccepted(string ownerId, ComposerAttachment attachment, AttachmentType type, UploadOutcome.Stored stored)
    {
        if (!_attachments.Contains(attachment))
        {
            stored.File.Dispose();
            return;
        }

        if (IsAlreadyReady(stored.Sha256))
        {
            stored.File.Dispose();
            attachment.MarkRejected(UploadRejections.AlreadyPending);
            return;
        }

        if (quota.MarkReadyIfFits(ownerId, stored) is QuotaCheck.Rejected quotaRejection)
        {
            attachment.MarkRejected(quotaRejection.Reason);
            return;
        }

        attachment.MarkReady(type, stored);
    }

    // Uploads in a tab run one after another, so every earlier copy of the
    // same content is already ready when this one is checked.
    private bool IsAlreadyReady(string sha256) =>
        _attachments.Any(attachment => attachment.Ready?.Sha256 == sha256);

    public void Remove(Guid attachmentId)
    {
        if (_attachments.Find(attachment => attachment.AttachmentId == attachmentId) is ComposerAttachment attachment)
        {
            Discard(attachment);
            Changed?.Invoke();
        }
    }

    // On send: the attachments ready now are this message's, and one that
    // finishes uploading later stays for the next.
    internal MessageAttachments AttachReadyToMessage()
    {
        var readyAttachmentIds = _attachments
            .Where(attachment => attachment.Ready is not null)
            .Select(attachment => attachment.AttachmentId)
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
        foreach (var attachment in _attachments.Where(attachment => attachmentIds.Contains(attachment.AttachmentId)).ToList())
        {
            if (attachment.Ready is ReadyAttachment ready)
            {
                _attachments.Remove(attachment);
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
        DiscardAll();
        SelectionNotice = null;
        Changed?.Invoke();
    }

    // The circuit is gone, so nothing can send what is pending. That can be a
    // few minutes after the tab closed or reloaded: Blazor keeps the circuit in
    // case it reconnects, and until then its files stay and count towards the
    // owner's quota.
    public void Dispose() => DiscardAll();

    private void DiscardAll()
    {
        foreach (var attachment in _attachments.ToList())
        {
            Discard(attachment);
        }
    }

    // Refused attachments have already said why; a new selection or a send
    // clears them, so they never pile up.
    private void DiscardRejected()
    {
        foreach (var attachment in _attachments.Where(attachment => attachment.Status == AttachmentStatus.Rejected).ToList())
        {
            Discard(attachment);
        }
    }

    // The one way an attachment leaves without being sent: its upload stops
    // and its file, if stored, is deleted. Cancelled, never disposed: the
    // upload may still be linking to its token.
    private void Discard(ComposerAttachment attachment)
    {
        _attachments.Remove(attachment);
        attachment.Cancellation.Cancel();
        attachment.Ready?.File.Dispose();
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
