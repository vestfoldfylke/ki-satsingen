using System.Diagnostics;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Pipeline;

namespace kisatsingen.Services.Assistants;

internal abstract record AssistantFileResult
{
    private AssistantFileResult() { }

    public sealed record Saved(KnowledgeFileMetadata File) : AssistantFileResult;

    // Shown to the user as is.
    public sealed record Rejected(string Reason) : AssistantFileResult;
}

// Adds one uploaded file to an assistant: runs the pipeline and saves the new
// file. Whether the user may edit the assistant (CanEditAssistant) is
// authorized before this is called. Throws only when cancelled; a save
// already committed by then stays.
internal sealed class AssistantFileProcessor(
    KnowledgeFilePipeline pipeline,
    IKnowledgeFileRepository knowledgeFiles,
    ILogger logger)
{
    private const string NotProcessedReason = "Filen kunne ikke behandles. Prøv igjen.";
    private const string NotSavedReason = "Filen kunne ikke lagres. Prøv igjen.";

    // Releases the file once it settles, so it stops counting against the
    // owner's quota.
    public async Task<AssistantFileResult> ProcessAsync(Guid ownerId, Guid assistantId, ReadyAttachment attachment, CancellationToken ct)
    {
        try
        {
            return await RunPipelineAsync(ownerId, assistantId, attachment, ct);
        }
        finally
        {
            attachment.File.Dispose();
        }
    }

    // A converter bug is that file's reason, not a failure of the page.
    private async Task<AssistantFileResult> RunPipelineAsync(Guid ownerId, Guid assistantId, ReadyAttachment attachment, CancellationToken ct)
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
                return await SaveAsync(ownerId, assistantId, completed.File, ct);
            case KnowledgeFilePipelineResult.Rejected pipelineRejection:
                return new AssistantFileResult.Rejected(pipelineRejection.Reason);
            default:
                throw new UnreachableException($"The pipeline returned {result.GetType().Name}, which the assistant file processor does not handle. Handle it here.");
        }
    }

    // A failed save, such as the assistant deleted meanwhile, is logged and
    // becomes that file's reason.
    private async Task<AssistantFileResult> SaveAsync(Guid ownerId, Guid assistantId, NewKnowledgeFile newFile, CancellationToken ct)
    {
        KnowledgeFileSaveResult saveResult;
        try
        {
            saveResult = await knowledgeFiles.CreateFileForAssistantAsync(ownerId, assistantId, newFile, ct);
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
