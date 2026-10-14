using System.Diagnostics;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Pipeline;

namespace kisatsingen.Services.Chat;

// Makes one sent file available in its chat: as the file already saved there
// with the same content, or by creating a draft and saving it. Every file
// settles, available or unavailable with a reason, so one bad file never
// fails the others or the turn; only a stop propagates.
internal sealed class TurnAttachmentProcessor(
    KnowledgeFilePipeline pipeline,
    IKnowledgeFileRepository knowledgeFiles,
    ILogger logger)
{
    // Releases the file once it settles: it then stops counting against the
    // owner's quota, while the answer may stream for minutes.
    public async Task<TurnAttachmentResult> ProcessAsync(string ownerId, Guid chatId, ReadyAttachment attachment, CancellationToken ct)
    {
        try
        {
            return await FindSavedFileAsync(ownerId, chatId, attachment, ct) is KnowledgeFileMetadata savedFile
                ? new TurnAttachmentResult.Available(savedFile)
                : await ProcessAsNewAsync(ownerId, chatId, attachment, ct);
        }
        finally
        {
            attachment.File.Dispose();
        }
    }

    // A failed lookup only loses the reuse: the file is processed as new, and
    // the unique index refuses a twin's save. A stop is not a failure, so it
    // propagates.
    private async Task<KnowledgeFileMetadata?> FindSavedFileAsync(string ownerId, Guid chatId, ReadyAttachment attachment, CancellationToken ct)
    {
        try
        {
            return await knowledgeFiles.FindChatFileBySha256Async(ownerId, chatId, attachment.Sha256, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not look up attachment {FileName} among the saved files of chat {ChatId}. It is processed as new.", attachment.FileName, chatId);
            return null;
        }
    }

    // Runs the pipeline for its draft and saves it. A converter bug is that
    // file's reason, not the turn's failure.
    private async Task<TurnAttachmentResult> ProcessAsNewAsync(string ownerId, Guid chatId, ReadyAttachment attachment, CancellationToken ct)
    {
        KnowledgeFilePipelineResult result;
        try
        {
            result = await pipeline.RunAsync(attachment, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Could not process attachment {FileName} ({ContentType}, {SizeBytes} bytes) in chat {ChatId}. It is sent as unavailable.", attachment.FileName, attachment.Type.ContentType, attachment.SizeBytes, chatId);
            return new TurnAttachmentResult.Unavailable(AttachmentTexts.NotProcessedReason);
        }

        switch (result)
        {
            case KnowledgeFilePipelineResult.Completed completed:
                return await SaveAsync(ownerId, chatId, attachment, completed.Draft);
            case KnowledgeFilePipelineResult.Rejected rejected:
                return new TurnAttachmentResult.Unavailable(rejected.Reason);
            default:
                throw new UnreachableException($"The pipeline returned {result.GetType().Name}, which the attachment processor does not handle. Handle it here.");
        }
    }

    // CancellationToken.None: a processed file is saved even if stop comes
    // now. A failed save is that file's reason, not the turn's failure; only
    // the save is caught, so a bug below still fails the turn.
    private async Task<TurnAttachmentResult> SaveAsync(string ownerId, Guid chatId, ReadyAttachment attachment, KnowledgeFileDraft draft)
    {
        KnowledgeFileSaveResult saveResult;
        try
        {
            saveResult = await knowledgeFiles.CreateFileForChatAsync(ownerId, chatId, draft, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not save attachment {FileName} in chat {ChatId}. It is sent as unavailable.", attachment.FileName, chatId);
            return new TurnAttachmentResult.Unavailable(AttachmentTexts.NotSavedReason);
        }

        switch (saveResult)
        {
            case KnowledgeFileSaveResult.Saved saved:
                return new TurnAttachmentResult.Available(saved.File);
            case KnowledgeFileSaveResult.Rejected rejected:
                return new TurnAttachmentResult.Unavailable(rejected.Reason);
            default:
                throw new UnreachableException($"Saving returned {saveResult.GetType().Name}, which the attachment processor does not handle. Handle it here.");
        }
    }
}
