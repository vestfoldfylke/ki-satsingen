using kisatsingen.AIFunctions.FileTools;
using kisatsingen.Constants;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Attachments;
using Microsoft.Extensions.AI;
using Vestfold.Extensions.Metrics.Services;

namespace kisatsingen.Services.Chat;

internal sealed record TurnObserver(
    Action TurnSaved,
    Action<Turn> Changed,
    Action<KnowledgeFileMetadata> KnowledgeFileAvailable);

// Knows nothing of the view; ChatSession decides what a snapshot means for the page.
//
// One write path for every ending: inserted as Running before the model is asked,
// updated once however it ends. So a crash mid-turn still leaves a row that says
// the turn never finished.
internal sealed class TurnRunner
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IChatModelCatalog _catalog;
    private readonly IChatRepository _repo;
    private readonly ChatManager _chatManager;
    private readonly IMetricsService _metrics;
    private readonly TurnStreamer _streamer;
    private readonly string _metricPrefix;
    private readonly ILogger _logger;
    private readonly IUserTokenUsageRepository _userTokenUsageRepository;
    private readonly TurnAttachmentProcessor _attachmentProcessor;
    private readonly FileToolFactory _fileTools;

    public TurnRunner(
        IAuthenticationService authenticationService,
        IChatModelCatalog catalog,
        IChatRepository repo,
        ChatManager chatManager,
        IMetricsService metrics,
        TurnStreamer streamer,
        string metricPrefix,
        ILogger logger,
        IUserTokenUsageRepository userTokenUsageRepository,
        TurnAttachmentProcessor attachmentProcessor,
        FileToolFactory fileTools)
    {
        _authenticationService = authenticationService;
        _catalog = catalog;
        _repo = repo;
        _chatManager = chatManager;
        _metrics = metrics;
        _streamer = streamer;
        _metricPrefix = metricPrefix;
        _logger = logger;
        _userTokenUsageRepository = userTokenUsageRepository;
        _attachmentProcessor = attachmentProcessor;
        _fileTools = fileTools;
    }

    // Throws only what the page must handle itself: an unauthenticated caller, an
    // allocation failure, and a metrics failure, surfaced rather than swallowed.
    public async Task<Turn> RunAsync(TurnInput input, TurnCancellation cancellation, TurnObserver observer)
    {
        var model = input.Model;
        var row = new TurnRow(input.ChatId, input.IsChatSaved);

        // Counted once, in the finally, so every send lands on exactly one outcome.
        // Starts as Failed so an exception nothing classified lands in the alerted bucket.
        var outcome = TurnOutcome.Failed;
        string? servedModelId = null;

        try
        {
            var attempt = await ExecuteAsync(row, input, cancellation, observer);
            outcome = attempt.Outcome;
            servedModelId = attempt.ServedModelId;

            await FinishAttemptAsync(row, attempt, model);
            return attempt.Turn;
        }
        // Rethrown: with no error boundary it ends the circuit, and the reload is what
        // the sign-in middleware redirects. Practically unreachable mid-chat: a
        // circuit's sign-in state is fixed when it starts.
        catch (UserNotAuthenticatedException)
        {
            outcome = TurnOutcome.Unauthenticated;
            throw;
        }
        finally
        {
            CountSend(outcome, servedModelId ?? model.ModelId, model.Key);
        }
    }

    // The catches only name the outcome and never act on it, so none can throw past it.
    private async Task<TurnAttempt> ExecuteAsync(
        TurnRow row,
        TurnInput input,
        TurnCancellation cancellation,
        TurnObserver observer)
    {
        var turn = input.Turn;
        var builder = new TurnBuilder(turn);
        var stage = TurnStage.Authenticating;

        try
        {
            var ownerId = await _authenticationService.RequireUserObjectIdentifierAsync();
            row.OwnerId = ownerId;

            stage = TurnStage.SavingMessage;
            var inserted = await InsertAsync(row, ownerId, turn, input.AssistantId, observer, cancellation.Token);

            stage = TurnStage.ProcessingAttachments;
            await ProcessAttachmentsAsync(inserted, input.SentAttachments.All, builder, observer, cancellation.Token);

            stage = TurnStage.Generating;
            var chatHasKnowledgeFiles = input.ChatHasKnowledgeFiles || builder.Attachments.Any(attachment => attachment.FileId is not null);
            await StreamAsync(inserted, input, builder, chatHasKnowledgeFiles, observer, cancellation.Token);

            // CancellationToken.None: the answer is complete, so a stop or a switch of
            // chat during this one write must not store it as stopped.
            stage = TurnStage.SavingResponse;
            var answered = builder.Finish(TurnStatus.Completed);
            await SaveAsync(inserted, answered, CancellationToken.None);

            // Only after the write: a turn is a success once its answer is stored.
            return new TurnAttempt(answered);
        }
        // The filter is load-bearing: a provider timeout is also an
        // OperationCanceledException, and without it outages would be filed as
        // user stops, invisible to failure alerts.
        catch (OperationCanceledException) when (cancellation.IsCancelled)
        {
            var status = cancellation switch
            {
                { IsDisconnect: true } => TurnStatus.Disconnected,
                { IsLeave: true } => TurnStatus.LeftChat,
                _ => TurnStatus.Stopped
            };

            return new TurnAttempt(builder.Finish(status));
        }
        // Swallowed, bugs included: they are logged whole, and taking the circuit
        // down would only cost the user their transcript. Nothing that is control
        // flow — Blazor's NavigationException above all — may ever be thrown in the try.
        //
        // Two pass through: an unauthenticated caller, which RunAsync counts, and an
        // allocation failure, which isn't this turn's fault and fails a retry the same
        // way. The latter is kept off the Failure counter, so the two don't reconcile.
        catch (Exception ex) when (ex is not (UserNotAuthenticatedException or OutOfMemoryException))
        {
            return new TurnAttempt(builder.Finish(TurnStatus.Failed, stage), ex);
        }
    }

    // Saved before counting: a metrics failure surfaces, and must not also leave the
    // stored turn unfinished.
    private async Task FinishAttemptAsync(TurnRow row, TurnAttempt attempt, ChatModel model)
    {
        LogFailure(row, attempt);

        if (attempt.ShouldSaveTurn)
        {
            await SaveEndedTurnAsync(row, attempt.Turn);
        }

        await InsertTokenUsageAsync(row, attempt, model);

        CountFailure(attempt);
    }

    private void LogFailure(TurnRow row, TurnAttempt attempt)
    {
        if (attempt is { Failure: { } error, Turn.FailedAt: { } stage })
        {
            _logger.LogError(error, "Chat turn failed during {Stage} for chat {ChatId}", stage, row.ChatId);
        }
    }

    private void CountFailure(TurnAttempt attempt)
    {
        if (attempt is { Failure: { } error, Turn.FailedAt: { } stage })
        {
            _metrics.Count(
                $"{_metricPrefix}_failure_total",
                "Failed chat turns, by stage and exception type",
                (MetricConstants.MetricsStageLabelName, TurnStageMetric.LabelValue(stage)),
                (MetricConstants.MetricsExceptionLabelName, error.GetType().Name));
        }
    }

    // Sets row.Inserted before reporting the turn saved, so a listener that throws
    // cannot leave a written turn that the ending path never updates.
    private async Task<InsertedTurn> InsertAsync(TurnRow row, Guid ownerId, Turn turn, Guid? assistantId, TurnObserver observer, CancellationToken ct)
    {
        // Stopped before anything was saved: keep it that way.
        ct.ThrowIfCancellationRequested();

        var isNewChat = !row.IsChatSaved;

        try
        {
            if (isNewChat)
            {
                await _chatManager.CreateChatAsync(row.ChatId, turn.Prompt, assistantId, ct);
            }

            await _repo.InsertTurnAsync(ownerId, row.ChatId, ChatTurnMapper.ToEntity(turn), ct);
        }
        // If saving the first message fails at any step, the new chat is deleted, so it
        // never opens empty. The next message creates the chat again under the same id.
        catch (Exception) when (isNewChat)
        {
            await DiscardChatAsync(row.ChatId);
            throw;
        }

        var inserted = new InsertedTurn(ownerId, row.ChatId);
        row.Inserted = inserted;
        observer.TurnSaved();
        return inserted;
    }

    // Swallows failures: the turn's own failure is the one to report.
    private async Task DiscardChatAsync(Guid chatId)
    {
        try
        {
            await _chatManager.DeleteAsync(chatId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete chat {ChatId} after its first message failed to save. If the chat was created, it will show as an empty chat.", chatId);
        }
    }

    // One file at a time, in the order they were attached, each shown as it
    // settles so a stop or a later failure of the turn cannot lose it. A stop
    // leaves the rest processing, and ending the turn marks them interrupted.
    private async Task ProcessAttachmentsAsync(
        InsertedTurn inserted,
        IReadOnlyList<ReadyAttachment> sentAttachments,
        TurnBuilder builder,
        TurnObserver observer,
        CancellationToken ct)
    {
        for (var index = 0; index < sentAttachments.Count; index++)
        {
            var result = await _attachmentProcessor.ProcessAsync(inserted.OwnerId, inserted.ChatId, sentAttachments[index], ct);

            builder.SettleAttachment(index, result);
            if (result is TurnAttachmentResult.Available available)
            {
                observer.KnowledgeFileAvailable(available.File);
            }

            observer.Changed(builder.Snapshot());
        }
    }

    // Built here, after the attachments settle, since the attachment line
    // needs each saved file's id.
    private Task StreamAsync(
        InsertedTurn inserted,
        TurnInput input,
        TurnBuilder builder,
        bool chatHasKnowledgeFiles,
        TurnObserver observer,
        CancellationToken ct)
    {
        var turn = input.Turn;
        var messages = TranscriptRequest.Build([.. input.EarlierTurns, builder.Snapshot()]);
        builder.SetRequestTokens(ContextTokenEstimator.Estimate(messages, turn.SystemPrompt));

        var runtime = _catalog.Resolve(input.Model.Key);

        // The snapshot the turn is stored with, so the record can't drift from what was sent.
        var options = runtime.CreateOptions();
        options.Instructions = turn.SystemPrompt;

        // Only for a chat with files: one without pays nothing for their
        // definitions, and the model is not tempted to call list_files.
        if (chatHasKnowledgeFiles)
        {
            options.Tools = [.. options.Tools ?? [], .. _fileTools.CreateForChat(inserted.OwnerId, inserted.ChatId)];
        }

        return _streamer.StreamAsync(runtime.Client, messages, options, builder, () => observer.Changed(builder.Snapshot()), ct);
    }

    private async Task SaveAsync(InsertedTurn inserted, Turn ended, CancellationToken ct)
    {
        await _repo.UpdateTurnAsync(inserted.OwnerId, inserted.ChatId, ChatTurnMapper.ToEntity(ended), ct);
        _chatManager.MarkTouched(inserted.ChatId, DateTimeOffset.UtcNow);
    }

    // CancellationToken.None: after a stop the turn's own is cancelled and would
    // abort the write that records it. Swallows failures so a lost stored copy
    // cannot replace the outcome the user is shown.
    private async Task SaveEndedTurnAsync(TurnRow row, Turn ended)
    {
        var inserted = row.Inserted;
        if (inserted is null)
        {
            return;
        }

        try
        {
            await SaveAsync(inserted, ended, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not save how turn {TurnId} in chat {ChatId} ended ({Status}). It reads back as unfinished.",
                ended.Id,
                inserted.ChatId,
                ended.Status);
        }
    }

    private async Task InsertTokenUsageAsync(TurnRow row, TurnAttempt attempt, ChatModel model)
    {
        var usage = attempt.Turn.Metadata?.Usage;

        if (usage is null)
        {
            return;
        }

        try
        {
            var ownerId = row.OwnerId ?? await _authenticationService.RequireUserObjectIdentifierAsync();
            await _userTokenUsageRepository.InsertTokenUsageAsync(
                ownerId,
                model.Provider,
                model.ModelId,
                usage.InputTokens,
                usage.OutputTokens,
                usage.EstimatedInputTokens,
                usage.EstimatedOutputTokens,
                attempt.Turn.Status,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not save token usage for turn {TurnId} in chat {ChatId} with status {Status}",
                attempt.Turn.Id,
                row.ChatId,
                attempt.Turn.Status);
        }
    }

    // One place builds the labels because Prometheus fixes a metric's label names
    // on first use and throws if a later call differs.
    private void CountSend(TurnOutcome outcome, string? modelId, ChatModelKey modelKey) =>
        _metrics.Count(
            $"{_metricPrefix}_send_total",
            "Chat send attempts, by outcome",
            (MetricConstants.MetricsModelLabelName, modelId ?? MetricConstants.MetricsModelUnknownLabelValue),
            (MetricConstants.MetricsModelKeyLabelName, modelKey.Value),
            (MetricConstants.MetricsResultLabelName, TurnOutcomeMetric.LabelValue(outcome)));

    // What is known of the turn's row so far; a turn that ends early may never
    // learn the rest.
    private sealed class TurnRow(Guid chatId, bool isChatSaved)
    {
        public Guid ChatId { get; } = chatId;

        // False for a new chat: it is saved with this turn.
        public bool IsChatSaved { get; } = isChatSaved;

        // Null until sign-in is confirmed.
        public Guid? OwnerId { get; set; }

        // Null until the turn's row is written.
        public InsertedTurn? Inserted { get; set; }
    }

    // Where the turn's row is, once it is written.
    private sealed record InsertedTurn(Guid OwnerId, Guid ChatId);
}
