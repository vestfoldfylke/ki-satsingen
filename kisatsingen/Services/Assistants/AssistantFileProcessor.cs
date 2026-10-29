using System.Diagnostics;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Pipeline;
using Microsoft.AspNetCore.Components.Forms;

namespace kisatsingen.Services.Assistants;

public abstract record AssistantFileResult
{
    private AssistantFileResult() { }

    public sealed record Saved(KnowledgeFileMetadata File) : AssistantFileResult;

    // Shown to the user as is.
    public sealed record Rejected(string Reason) : AssistantFileResult;
}

// Adds one file selected in the browser to an assistant: room in the quota,
// the upload, processing and the save, with the temp file always deleted.
// Whether the user may edit the assistant (CanEditAssistant) is authorized
// before this is called. Throws only when cancelled; a save already
// committed by then stays.
public sealed class AssistantFileProcessor(
    AttachmentUploader uploader,
    AttachmentQuota quota,
    KnowledgeFilePipeline pipeline,
    IKnowledgeFileRepository knowledgeFiles,
    AttachmentMetrics metrics,
    ILogger<AssistantFileProcessor> logger)
{
    private const string NotProcessedReason = "Filen kunne ikke behandles. Prøv igjen.";
    private const string NotSavedReason = "Filen kunne ikke lagres. Prøv igjen.";

    public async Task<AssistantFileResult> ProcessAsync(
        Guid userId,
        Guid assistantId,
        IBrowserFile file,
        AttachmentType acceptedType,
        Action<long> onBytesReceived,
        Action onProcessing,
        CancellationToken ct)
    {
        var attachmentId = Guid.NewGuid();
        var upload = await UploadWithinQuotaAsync(userId, attachmentId, file, acceptedType, onBytesReceived, ct);
        switch (upload)
        {
            case UploadOutcome.Rejected rejected:
                metrics.CountRejected(rejected.Reason);
                return new AssistantFileResult.Rejected(rejected.Reason.Message);
            case UploadOutcome.Stored stored:
                try
                {
                    onProcessing();
                    var attachment = new ReadyAttachment(attachmentId, AttachmentFileName.Normalize(file.Name), acceptedType, stored.SizeBytes, stored.Sha256, stored.File);
                    return await ProcessStoredAsync(userId, assistantId, attachment, ct);
                }
                finally
                {
                    stored.File.Dispose();
                }
            default:
                throw new UnreachableException($"The upload returned {upload.GetType().Name}, which the assistant file processor does not handle. Handle it here.");
        }
    }

    private async Task<UploadOutcome> UploadWithinQuotaAsync(
        Guid userId,
        Guid attachmentId,
        IBrowserFile file,
        AttachmentType acceptedType,
        Action<long> onBytesReceived,
        CancellationToken ct)
    {
        if (quota.CheckDeclaredSizeFits(userId, file.Size) is QuotaCheck.Rejected startRejection)
        {
            return new UploadOutcome.Rejected(startRejection.Reason);
        }

        var outcome = await uploader.UploadAsync(userId, attachmentId, file, acceptedType, new UploadProgress(onBytesReceived), ct);
        if (outcome is not UploadOutcome.Stored stored)
        {
            return outcome;
        }

        return quota.MarkReadyIfFits(userId, stored) is QuotaCheck.Rejected finishRejection
            ? new UploadOutcome.Rejected(finishRejection.Reason)
            : stored;
    }

    // A converter bug is that file's reason, not a failure of the page.
    private async Task<AssistantFileResult> ProcessStoredAsync(Guid userId, Guid assistantId, ReadyAttachment attachment, CancellationToken ct)
    {
        KnowledgeFilePipelineResult result;
        try
        {
            result = await pipeline.RunAsync(attachment, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Could not process file {FileName} ({ContentType}, {SizeBytes} bytes) for assistant {AssistantId}.", attachment.FileName, attachment.Type.ContentType, attachment.SizeBytes, assistantId);
            return new AssistantFileResult.Rejected(NotProcessedReason);
        }

        switch (result)
        {
            case KnowledgeFilePipelineResult.Completed completed:
                return await SaveAsync(userId, assistantId, completed.File, ct);
            case KnowledgeFilePipelineResult.Rejected pipelineRejection:
                return new AssistantFileResult.Rejected(pipelineRejection.Reason);
            default:
                throw new UnreachableException($"The pipeline returned {result.GetType().Name}, which the assistant file processor does not handle. Handle it here.");
        }
    }

    // A failed save, such as the assistant deleted meanwhile, is logged and
    // becomes that file's reason.
    private async Task<AssistantFileResult> SaveAsync(Guid userId, Guid assistantId, NewKnowledgeFile newFile, CancellationToken ct)
    {
        KnowledgeFileSaveResult saveResult;
        try
        {
            saveResult = await knowledgeFiles.CreateFileForAssistantAsync(userId, assistantId, newFile, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Could not save file {FileName} for assistant {AssistantId}.", newFile.FileName, assistantId);
            return new AssistantFileResult.Rejected(NotSavedReason);
        }

        switch (saveResult)
        {
            case KnowledgeFileSaveResult.Saved saved:
                return new AssistantFileResult.Saved(saved.File);
            case KnowledgeFileSaveResult.Rejected saveRejection:
                return new AssistantFileResult.Rejected(saveRejection.Reason);
            default:
                throw new UnreachableException($"Saving returned {saveResult.GetType().Name}, which the assistant file processor does not handle. Handle it here.");
        }
    }
}
