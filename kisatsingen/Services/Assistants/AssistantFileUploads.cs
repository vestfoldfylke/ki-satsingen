using System.Diagnostics;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Pipeline;
using Microsoft.AspNetCore.Components.Forms;

namespace kisatsingen.Services.Assistants;

// The files being added to an assistant on its page, for one tab: listed when
// selected, then uploaded and processed one at a time, and gone from here once
// saved.
//
// No lock: it is only ever used from its circuit's dispatcher, which runs one
// call at a time. Blazor runs UI events, the awaits that resume from them, and
// disposal there.
public sealed class AssistantFileUploads(
    IAuthenticationService authenticationService,
    AttachmentUploader uploader,
    AttachmentQuota quota,
    KnowledgeFilePipeline pipeline,
    IKnowledgeFileRepository knowledgeFiles,
    AttachmentMetrics metrics,
    ILogger<AssistantFileUploads> logger) : IDisposable
{
    private readonly AssistantFileProcessor _processor = new(pipeline, knowledgeFiles, logger);
    private readonly List<AssistantFileUpload> _uploads = [];

    // Replaced on reset, never disposed: an upload may still be linking to its token.
    private CancellationTokenSource _cancellation = new();

    private Guid? _ownerId;

    public event Action? Changed;

    // About the selection as a whole, which no single file can carry.
    public string? SelectionNotice { get; private set; }

    public IReadOnlyList<AssistantFileUpload> All => _uploads;

    // Holds the picker: a new selection makes the files of the one still
    // uploading unreadable.
    public bool IsUploading =>
        _uploads.Any(upload => upload.Status != AssistantFileUploadStatus.Rejected);

    // Whether the user may edit the assistant (CanEditAssistant) is authorized
    // before this is called. savedFileCount is how many files the assistant
    // has, for the room left; onFileSaved runs after each save, before the
    // file leaves All.
    public async Task AddAsync(
        Guid assistantId,
        InputFileChangeEventArgs selection,
        int savedFileCount,
        Func<Task> onFileSaved)
    {
        // This selection's own, so a reset stops it but not the next one.
        var ct = _cancellation.Token;
        var ownerId = await EnsureOwnerAsync();

        // Checked before GetMultipleFiles, which throws above its limit.
        if (selection.FileCount > AttachmentOptions.MaxFilesPerSelection)
        {
            SelectionNotice = UploadRejections.TooManyInSelection(AttachmentOptions.MaxFilesPerSelection).Message;
            Changed?.Invoke();
            return;
        }

        SelectionNotice = null;
        _uploads.RemoveAll(upload => upload.Status == AssistantFileUploadStatus.Rejected);
        var waiting = ListSelection(selection, savedFileCount);
        Changed?.Invoke();

        // One at a time: they share the circuit's connection, so in parallel
        // they would only take turns on it.
        foreach (var (upload, file, type) in waiting)
        {
            try
            {
                await AddOneAsync(ownerId, assistantId, upload, file, type, onFileSaved, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }

            Changed?.Invoke();
        }
    }

    // Stops the selection in progress and drops the files not saved yet; a
    // file already saved stays.
    public void Reset()
    {
        _cancellation.Cancel();
        _cancellation = new CancellationTokenSource();
        _uploads.Clear();
        SelectionNotice = null;
        Changed?.Invoke();
    }

    // Only a refusal: a file still in progress cannot be stopped on its own.
    public void Dismiss(Guid uploadId)
    {
        if (_uploads.Find(upload => upload.Id == uploadId) is { Status: AssistantFileUploadStatus.Rejected } upload)
        {
            _uploads.Remove(upload);
            Changed?.Invoke();
        }
    }

    public void Dispose() => _cancellation.Cancel();

    // All listed up front, so the whole selection shows at once and a file
    // that is not acceptable is refused before any bytes move.
    private List<(AssistantFileUpload Upload, IBrowserFile File, AttachmentType Type)> ListSelection(
        InputFileChangeEventArgs selection,
        int savedFileCount)
    {
        var roomLeft = Assistant.MaxKnowledgeFiles - savedFileCount;
        var waiting = new List<(AssistantFileUpload Upload, IBrowserFile File, AttachmentType Type)>();
        foreach (var file in selection.GetMultipleFiles(AttachmentOptions.MaxFilesPerSelection))
        {
            var fileName = AttachmentFileName.Normalize(file.Name);
            var upload = new AssistantFileUpload(fileName, file.Size);
            _uploads.Add(upload);

            switch (uploader.Check(fileName, file.Size))
            {
                case UploadCheck.Rejected rejected:
                    Reject(upload, rejected.Reason);
                    break;
                case UploadCheck.Accepted when waiting.Count >= roomLeft:
                    upload.MarkRejected(KnowledgeFileSaveRejections.AssistantFull);
                    break;
                case UploadCheck.Accepted accepted:
                    waiting.Add((upload, file, accepted.Type));
                    break;
            }
        }

        return waiting;
    }

    // Throws only when cancelled; a save already committed by then stays.
    private async Task AddOneAsync(
        Guid ownerId,
        Guid assistantId,
        AssistantFileUpload upload,
        IBrowserFile file,
        AttachmentType type,
        Func<Task> onFileSaved,
        CancellationToken ct)
    {
        // A reset during the previous file's onFileSaved, which takes no
        // token, is first seen here.
        ct.ThrowIfCancellationRequested();

        var attachmentId = Guid.NewGuid();
        var outcome = await UploadWithinQuotaAsync(ownerId, attachmentId, upload, file, type, ct);
        switch (outcome)
        {
            case UploadOutcome.Rejected rejected:
                Reject(upload, rejected.Reason);
                return;
            case UploadOutcome.Stored stored:
                upload.MarkProcessing();
                Changed?.Invoke();
                var attachment = new ReadyAttachment(attachmentId, upload.FileName, type, stored.SizeBytes, stored.Sha256, stored.File);
                await ProcessAsync(ownerId, assistantId, upload, attachment, onFileSaved, ct);
                return;
            default:
                throw new UnreachableException($"The upload returned {outcome.GetType().Name}, which the assistant's uploads do not handle. Handle it here.");
        }
    }

    private async Task ProcessAsync(
        Guid ownerId,
        Guid assistantId,
        AssistantFileUpload upload,
        ReadyAttachment attachment,
        Func<Task> onFileSaved,
        CancellationToken ct)
    {
        var result = await _processor.ProcessAsync(ownerId, assistantId, attachment, ct);
        switch (result)
        {
            case AssistantFileResult.Rejected rejected:
                upload.MarkRejected(rejected.Reason);
                return;
            case AssistantFileResult.Saved:
                await onFileSaved();
                _uploads.Remove(upload);
                return;
            default:
                throw new UnreachableException($"The processor returned {result.GetType().Name}, which the assistant's uploads do not handle. Handle it here.");
        }
    }

    private async Task<UploadOutcome> UploadWithinQuotaAsync(
        Guid ownerId,
        Guid attachmentId,
        AssistantFileUpload upload,
        IBrowserFile file,
        AttachmentType type,
        CancellationToken ct)
    {
        if (quota.CheckDeclaredSizeFits(ownerId, file.Size) is QuotaCheck.Rejected startRejection)
        {
            return new UploadOutcome.Rejected(startRejection.Reason);
        }

        var progress = new UploadProgress(receivedBytes =>
        {
            if (upload.ReportProgress(receivedBytes))
            {
                Changed?.Invoke();
            }
        });
        var outcome = await uploader.UploadAsync(ownerId, attachmentId, file, type, progress, ct);
        if (outcome is not UploadOutcome.Stored stored)
        {
            return outcome;
        }

        return quota.MarkReadyIfFits(ownerId, stored) is QuotaCheck.Rejected finishRejection
            ? new UploadOutcome.Rejected(finishRejection.Reason)
            : stored;
    }

    // Every refusal from the upload stack goes through here, so none is left uncounted.
    private void Reject(AssistantFileUpload upload, UploadRejection rejection)
    {
        upload.MarkRejected(rejection.Message);
        metrics.CountRejected(rejection);
    }

    private async Task<Guid> EnsureOwnerAsync() =>
        _ownerId ??= await authenticationService.RequireUserObjectIdentifierAsync();
}
