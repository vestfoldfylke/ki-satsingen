using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Processing;

namespace kisatsingen.Services.Chat;

// Makes a turn's files available in its chat. A file whose content is already
// saved there is available as that file; the rest are processed and saved.
// Every file settles on its own, available or unavailable with a reason, so
// one bad file never fails the others or the turn; only a stop ends the step
// early. Each file is reported the moment it becomes available, not with the
// result, so a stop or a later failure of the turn cannot lose it.
internal sealed class TurnAttachmentStep(
    KnowledgeFileProcessingQueue queue,
    IKnowledgeFileRepository knowledgeFiles,
    TempFileStore tempFiles,
    ILogger logger)
{
    private sealed record JobOutcome(int Index, KnowledgeFileProcessingResult? Result)
    {
        [MemberNotNullWhen(false, nameof(Result))]
        public bool WasStopped => Result is null;
    }

    // Both callbacks are called on the turn's own flow, so the caller can show
    // what they report; never from a worker thread.
    public async Task<IReadOnlyList<TurnAttachment>> RunAsync(
        string ownerId,
        Guid chatId,
        IReadOnlyList<ReadyAttachment> readyAttachments,
        Action<IReadOnlyList<TurnAttachment>> onChanged,
        Action<KnowledgeFileMetadata> onKnowledgeFileAvailable,
        CancellationToken ct)
    {
        // Shown before the lookup, so the bubble lists the files at once.
        var turnAttachments = readyAttachments.Select(attachment => TurnAttachment.Processing(attachment.FileName)).ToArray();
        onChanged([.. turnAttachments]);

        IReadOnlyDictionary<string, KnowledgeFileMetadata> savedFilesBySha256;
        try
        {
            savedFilesBySha256 = await FindSavedFilesBySha256Async(ownerId, chatId, ct);
        }
        // No job exists yet to delete the temp files the turn has taken.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            DeleteTempFiles(readyAttachments);
            throw;
        }

        var attachmentsToProcess = SettleAlreadySaved(readyAttachments, turnAttachments, savedFilesBySha256, onKnowledgeFileAvailable);
        onChanged([.. turnAttachments]);

        // All handed to the queue at once: its workers and the per-user limit
        // decide how many run together.
        var jobs = attachmentsToProcess.Select(toProcess => ProcessAsync(ownerId, chatId, toProcess.Attachment, toProcess.Index, ct)).ToList();

        // One at a time, in the order they finish, so saving and updating the
        // turn never run in parallel.
        await foreach (var completedJob in Task.WhenEach(jobs))
        {
            var outcome = await completedJob;
            if (outcome.WasStopped)
            {
                continue;
            }

            turnAttachments[outcome.Index] = await SettleAsync(ownerId, chatId, turnAttachments[outcome.Index], outcome.Result, onKnowledgeFileAvailable);
            onChanged([.. turnAttachments]);
        }

        // After the loop, so every file that finished before the stop is saved.
        ct.ThrowIfCancellationRequested();
        return turnAttachments;
    }

    // A failed lookup only loses the reuse: the files are processed as new,
    // and the unique index refuses a twin's save. A stop is not a failure, so
    // it propagates.
    private async Task<IReadOnlyDictionary<string, KnowledgeFileMetadata>> FindSavedFilesBySha256Async(string ownerId, Guid chatId, CancellationToken ct)
    {
        try
        {
            var savedFiles = await knowledgeFiles.ListFilesForChatAsync(ownerId, chatId, ct);

            // The unique index allows one file per content, but a broken row
            // must not read as a failed lookup: the first one wins.
            return savedFiles.DistinctBy(file => file.Sha256).ToDictionary(file => file.Sha256);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not look up the saved files of chat {ChatId}. Its attachments are processed as new.", chatId);
            return new Dictionary<string, KnowledgeFileMetadata>();
        }
    }

    private void DeleteTempFiles(IEnumerable<ReadyAttachment> readyAttachments)
    {
        foreach (var attachment in readyAttachments)
        {
            tempFiles.Delete(attachment.File);
        }
    }

    // Settles every attachment already saved in the chat as available, and
    // returns the rest, with their place in the turn, for processing. No job
    // will run for a settled one, so its temp file is deleted here.
    private List<(int Index, ReadyAttachment Attachment)> SettleAlreadySaved(
        IReadOnlyList<ReadyAttachment> readyAttachments,
        TurnAttachment[] turnAttachments,
        IReadOnlyDictionary<string, KnowledgeFileMetadata> savedFilesBySha256,
        Action<KnowledgeFileMetadata> onKnowledgeFileAvailable)
    {
        var attachmentsToProcess = new List<(int Index, ReadyAttachment Attachment)>();
        for (var index = 0; index < readyAttachments.Count; index++)
        {
            var attachment = readyAttachments[index];
            if (!savedFilesBySha256.TryGetValue(attachment.Sha256, out var savedFile))
            {
                attachmentsToProcess.Add((index, attachment));
                continue;
            }

            turnAttachments[index] = turnAttachments[index].Available(savedFile.Id);
            onKnowledgeFileAvailable(savedFile);
            tempFiles.Delete(attachment.File);
        }

        return attachmentsToProcess;
    }

    // A stop comes back as a null result rather than an exception, so the loop
    // still settles the files that finished before it. A failed job is that
    // file's reason, like a failed save.
    private async Task<JobOutcome> ProcessAsync(string ownerId, Guid chatId, ReadyAttachment attachment, int index, CancellationToken ct)
    {
        try
        {
            var result = await queue.ProcessAsync(ownerId, attachment, NoProgress.Instance, ct);
            return new JobOutcome(index, result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new JobOutcome(index, Result: null);
        }
        // A warning: the worker has already logged a failed job as an error.
        // Logged here too, since a failure before the job reaches a worker has
        // no other log.
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not process attachment {FileName} in chat {ChatId}. It is sent as unavailable.", attachment.FileName, chatId);
            return new JobOutcome(index, new KnowledgeFileProcessingResult.Rejected(AttachmentTexts.NotProcessedReason));
        }
    }

    private async Task<TurnAttachment> SettleAsync(
        string ownerId,
        Guid chatId,
        TurnAttachment attachment,
        KnowledgeFileProcessingResult result,
        Action<KnowledgeFileMetadata> onKnowledgeFileAvailable)
    {
        switch (result)
        {
            case KnowledgeFileProcessingResult.Processed processed:
                return await SaveAsync(ownerId, chatId, attachment, processed.Draft, onKnowledgeFileAvailable);
            case KnowledgeFileProcessingResult.Rejected rejected:
                return attachment.Unavailable(rejected.Reason);
            default:
                throw new UnreachableException($"Processing returned {result.GetType().Name}, which the attachment step does not handle. Handle it here.");
        }
    }

    // CancellationToken.None: a processed file is saved even if stop comes
    // now. A failed save is that file's reason, not the turn's failure; only
    // the save is caught, so a bug below still fails the turn.
    private async Task<TurnAttachment> SaveAsync(
        string ownerId,
        Guid chatId,
        TurnAttachment attachment,
        KnowledgeFileDraft draft,
        Action<KnowledgeFileMetadata> onKnowledgeFileAvailable)
    {
        KnowledgeFileSaveResult saveResult;
        try
        {
            saveResult = await knowledgeFiles.CreateFileForChatAsync(ownerId, chatId, draft, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not save attachment {FileName} in chat {ChatId}. It is sent as unavailable.", attachment.FileName, chatId);
            return attachment.Unavailable(AttachmentTexts.NotSavedReason);
        }

        switch (saveResult)
        {
            case KnowledgeFileSaveResult.Saved saved:
                onKnowledgeFileAvailable(saved.File);
                return attachment.Available(saved.File.Id);
            case KnowledgeFileSaveResult.Rejected rejected:
                return attachment.Unavailable(rejected.Reason);
            default:
                throw new UnreachableException($"Saving returned {saveResult.GetType().Name}, which the attachment step does not handle. Handle it here.");
        }
    }

    // The bubble shows only processing, available or unavailable: every
    // current format processes in milliseconds, so finer stages would flash
    // past unseen. Report them once a converter is slow enough to show them.
    private sealed class NoProgress : IProgress<KnowledgeFileProcessingStage>
    {
        public static readonly NoProgress Instance = new();

        public void Report(KnowledgeFileProcessingStage value)
        {
        }
    }
}
