using kisatsingen.Services.Attachments;
using Microsoft.AspNetCore.Components.Forms;

namespace kisatsingen.Services.Chat;

// The open chat view's attachments, for one tab: selected in the composer,
// uploaded to temp files, and owned here until they are sent or discarded.
//
// No lock: it is only ever used from its circuit's dispatcher, which runs one
// call at a time. Blazor runs UI events, the awaits that resume from them, and
// disposal there.
public sealed class ChatAttachments(
    IAuthenticationService authenticationService,
    AttachmentUploader uploader,
    AttachmentQuota quota,
    AttachmentMetrics metrics) : IDisposable
{
    private readonly List<ComposerAttachment> _attachments = [];

    private Guid? _ownerId;

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
        if (selection.FileCount > AttachmentOptions.MaxFilesPerSelection)
        {
            SelectionNotice = UploadRejections.TooManyInSelection(AttachmentOptions.MaxFilesPerSelection).Message;
            Changed?.Invoke();
            return;
        }

        SelectionNotice = null;
        DiscardRejected();

        // All listed up front, so the whole selection shows at once and a file
        // that is not acceptable is refused before any bytes move.
        var selectedAttachments = new List<ComposerAttachment>();
        var waiting = new List<(ComposerAttachment Attachment, IBrowserFile File, AttachmentType Type)>();
        foreach (var file in selection.GetMultipleFiles(AttachmentOptions.MaxFilesPerSelection))
        {
            var fileName = AttachmentFileName.Normalize(file.Name);
            var attachment = new ComposerAttachment(fileName, file.Size);
            _attachments.Add(attachment);
            selectedAttachments.Add(attachment);

            switch (uploader.Check(fileName, file.Size))
            {
                case UploadCheck.Accepted accepted:
                    waiting.Add((attachment, file, accepted.Type));
                    break;
                case UploadCheck.Rejected rejected:
                    attachment.MarkRejected(rejected.Reason);
                    break;
            }
        }

        Changed?.Invoke();

        // One at a time: they share the circuit's connection, so in parallel
        // they would only take turns on it.
        foreach (var (attachment, file, type) in waiting)
        {
            await AttachOneAsync(ownerId, attachment, file, type);
            Changed?.Invoke();
        }

        // Counted here, from the final status, so no way of refusing a file can
        // be missed; from the selection's own list, since a reset or the
        // circuit closing can empty _attachments meanwhile.
        foreach (var attachment in selectedAttachments)
        {
            if (attachment.Rejection is UploadRejection rejection)
            {
                metrics.CountRejected(rejection);
            }
        }
    }

    // Never throws: this runs in a UI event handler, where an exception would
    // end the circuit. Announces the upload starting and its progress; the
    // outcome is announced by the loop that runs it.
    private async Task AttachOneAsync(Guid ownerId, ComposerAttachment attachment, IBrowserFile file, AttachmentType type)
    {
        // Removed, or the view moved on, while it waited for its turn.
        if (!_attachments.Contains(attachment))
        {
            return;
        }

        var cancellation = attachment.Cancellation.Token;
        var progress = new UploadProgress(receivedBytes =>
        {
            if (attachment.ReportProgress(receivedBytes))
            {
                Changed?.Invoke();
            }
        });

        try
        {
            // Checked as the upload starts rather than when selected: uploads in a
            // tab run one after another, so previously attached files are on disk
            // at their real size.
            if (quota.CheckDeclaredSizeFits(ownerId, attachment.DeclaredSizeBytes) is QuotaCheck.Rejected quotaRejection)
            {
                attachment.MarkRejected(quotaRejection.Reason);
                return;
            }

            attachment.MarkUploading();
            Changed?.Invoke();

            var outcome = await uploader.UploadAsync(ownerId, attachment.AttachmentId, file, type, progress, cancellation);

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
    }

    // Takes the file in every case: marked ready when the attachment is still
    // here, is no duplicate and fits the quota; deleted otherwise. A file the
    // quota refuses is already deleted by the quota.
    private void MarkReadyIfAccepted(Guid ownerId, ComposerAttachment attachment, AttachmentType type, UploadOutcome.Stored stored)
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

    // On send: the ready attachments' files are the turn's from here. The
    // refusals go too: they were about the selection just sent.
    internal SentAttachments TakeReady()
    {
        var sent = new List<ReadyAttachment>();
        foreach (var attachment in _attachments.ToList())
        {
            if (attachment.Ready is ReadyAttachment ready)
            {
                _attachments.Remove(attachment);
                sent.Add(ready);
            }
        }

        DiscardRejected();
        Changed?.Invoke();
        return new SentAttachments(sent);
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

    private async Task<Guid> EnsureOwnerAsync() =>
        _ownerId ??= await authenticationService.RequireUserObjectIdentifierAsync();
}
