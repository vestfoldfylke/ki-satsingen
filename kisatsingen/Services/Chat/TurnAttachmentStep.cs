using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Processing;

namespace kisatsingen.Services.Chat;

// Processes a turn's files and saves them into its chat. Every file settles on
// its own, saved, or unavailable with a reason, so one bad file never fails
// the others or the turn; only a stop ends the step early.
internal sealed class TurnAttachmentStep(KnowledgeFileProcessingQueue queue, IKnowledgeFileRepository knowledgeFiles, ILogger logger)
{
    private sealed record JobOutcome(int Index, KnowledgeFileProcessingResult? Result)
    {
        [MemberNotNullWhen(false, nameof(Result))]
        public bool WasStopped => Result is null;
    }

    // onChanged is called on the turn's own flow after every change, so the
    // caller can show it; never from a worker thread.
    public async Task<IReadOnlyList<TurnAttachment>> RunAsync(
        string ownerId,
        Guid chatId,
        IReadOnlyList<ReadyAttachment> readyAttachments,
        Action<IReadOnlyList<TurnAttachment>> onChanged,
        CancellationToken ct)
    {
        var turnAttachments = readyAttachments.Select(attachment => TurnAttachment.Processing(attachment.FileName)).ToArray();
        onChanged([.. turnAttachments]);

        // All handed to the queue at once: its workers and the per-user limit
        // decide how many run together.
        var jobs = readyAttachments.Select((attachment, index) => ProcessAsync(ownerId, attachment, index, ct)).ToList();

        // One at a time, in the order they finish, so saving and updating the
        // turn never run in parallel.
        await foreach (var completedJob in Task.WhenEach(jobs))
        {
            var outcome = await completedJob;
            if (outcome.WasStopped)
            {
                continue;
            }

            turnAttachments[outcome.Index] = await SettleAsync(ownerId, chatId, turnAttachments[outcome.Index], outcome.Result);
            onChanged([.. turnAttachments]);
        }

        // After the loop, so every file that finished before the stop is saved.
        ct.ThrowIfCancellationRequested();
        return turnAttachments;
    }

    // A stop comes back as a null result rather than an exception, so the loop
    // still settles the files that finished before it.
    private async Task<JobOutcome> ProcessAsync(string ownerId, ReadyAttachment attachment, int index, CancellationToken ct)
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
    }

    private async Task<TurnAttachment> SettleAsync(
        string ownerId,
        Guid chatId,
        TurnAttachment attachment,
        KnowledgeFileProcessingResult result)
    {
        switch (result)
        {
            case KnowledgeFileProcessingResult.Processed processed:
                return await SaveAsync(ownerId, chatId, attachment, processed.Draft);
            case KnowledgeFileProcessingResult.Rejected rejected:
                return attachment.Unavailable(rejected.Reason);
            default:
                throw new UnreachableException($"Processing returned {result.GetType().Name}, which the attachment step does not handle. Handle it here.");
        }
    }

    // CancellationToken.None: a processed file is saved even if stop comes
    // now. A failed save is that file's reason, not the turn's failure; only
    // the save is caught, so a bug below still fails the turn.
    private async Task<TurnAttachment> SaveAsync(string ownerId, Guid chatId, TurnAttachment attachment, KnowledgeFileDraft draft)
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
                return attachment.SavedAs(saved.File.Id);
            case KnowledgeFileSaveResult.Rejected rejected:
                return attachment.Unavailable(rejected.Reason);
            default:
                throw new UnreachableException($"Saving returned {saveResult.GetType().Name}, which the attachment step does not handle. Handle it here.");
        }
    }

    // Progress is for the chips on the user's bubble, which step 4b adds.
    private sealed class NoProgress : IProgress<KnowledgeFileProcessingStage>
    {
        public static readonly NoProgress Instance = new();

        public void Report(KnowledgeFileProcessingStage value)
        {
        }
    }
}
