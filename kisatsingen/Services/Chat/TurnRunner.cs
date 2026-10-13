using kisatsingen.AIFunctions.FileTools;
using kisatsingen.Constants;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Attachments;
using Microsoft.Extensions.AI;
using Vestfold.Extensions.Metrics.Services;

namespace kisatsingen.Services.Chat;

// ChatPersisted fires mid-turn so a new chat's id can reach the URL before the answer ends.
internal sealed record TurnObserver(
    Action<Guid> ChatPersisted,
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
    private readonly TurnAttachmentStep _attachmentStep;
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
        TurnAttachmentStep attachmentStep,
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
        _attachmentStep = attachmentStep;
        _fileTools = fileTools;
    }

    // Throws only what the page must handle itself: an unauthenticated caller, an
    // allocation failure, and a metrics failure, surfaced rather than swallowed.
    public async Task<Turn> RunAsync(TurnInput input, TurnCancellation cancellation, TurnObserver observer)
    {
        var model = input.Model;
        var row = new TurnRow(input.ExistingChatId);

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
            var inserted = await InsertAsync(row, ownerId, turn, observer, cancellation.Token);

            stage = TurnStage.ProcessingAttachments;
            var takenAttachments = TakeMessageAttachments(input.MessageAttachments, cancellation.Token);
            var turnAttachments = await ProcessAttachmentsAsync(inserted, takenAttachments, builder, observer, cancellation.Token);

            stage = TurnStage.Generating;
            var chatHasKnowledgeFiles = input.ChatHasKnowledgeFiles || turnAttachments.Any(attachment => attachment.FileId is not null);
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
            _logger.LogError(error, "Chat turn failed during {Stage} for chat {ChatId}", stage, row.KnownChatId);
        }
    }

    private void CountFailure(TurnAttempt attempt)
    {
        if (attempt is { Failure: { } error, Turn.FailedAt: { } stage })
        {
            _metrics.Count(
                $"{_metricPrefix}_Failure",
                "Failed chat turns, by stage and exception type",
                (MetricConstants.MetricsStageLabelName, stage.ToString()),
                (MetricConstants.MetricsExceptionLabelName, error.GetType().Name));
        }
    }

    // Sets row.Inserted before announcing the chat, so a listener that throws
    // cannot leave a written turn that the ending path never updates.
    private async Task<InsertedTurn> InsertAsync(TurnRow row, string ownerId, Turn turn, TurnObserver observer, CancellationToken ct)
    {
        // Stopped before anything was saved: keep it that way.
        ct.ThrowIfCancellationRequested();

        var isNewChat = row.ExistingChatId is null;
        var chatId = await _chatManager.EnsurePersistedAsync(row.ExistingChatId, turn.Prompt, ct);

        try
        {
            await _repo.InsertTurnAsync(ownerId, chatId, ChatTurnMapper.ToEntity(turn), ct);
        }
        // A new chat whose first question did not save would open empty, so it goes.
        // Announced only after this, so neither the URL nor the view ever held it.
        catch (Exception) when (isNewChat)
        {
            await DiscardChatAsync(chatId);
            throw;
        }

        var inserted = new InsertedTurn(ownerId, chatId);
        row.Inserted = inserted;
        observer.ChatPersisted(chatId);
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
            _logger.LogWarning(ex, "Could not delete chat {ChatId} after its first question failed to save. It will show as an empty chat.", chatId);
        }
    }

    // From here the turn owns their temp files.
    private static IReadOnlyList<ReadyAttachment> TakeMessageAttachments(MessageAttachments messageAttachments, CancellationToken ct)
    {
        // Stopped before they were taken: they stay in the composer.
        ct.ThrowIfCancellationRequested();
        return messageAttachments.TakeReady();
    }

    private async Task<IReadOnlyList<TurnAttachment>> ProcessAttachmentsAsync(
        InsertedTurn inserted,
        IReadOnlyList<ReadyAttachment> takenAttachments,
        TurnBuilder builder,
        TurnObserver observer,
        CancellationToken ct)
    {
        if (takenAttachments.Count == 0)
        {
            return [];
        }

        return await _attachmentStep.RunAsync(
            inserted.OwnerId,
            inserted.ChatId,
            takenAttachments,
            attachments =>
            {
                builder.SetAttachments(attachments);
                observer.Changed(builder.Snapshot());
            },
            observer.KnowledgeFileAvailable,
            ct);
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
                row.KnownChatId,
                attempt.Turn.Status);
        }
    }

    // One place builds the labels because Prometheus fixes a metric's label names
    // on first use and throws if a later call differs.
    private void CountSend(TurnOutcome outcome, string? modelId, ChatModelKey modelKey) =>
        _metrics.Count(
            $"{_metricPrefix}_Send",
            "Chat send attempts, by outcome",
            (MetricConstants.MetricsModelLabelName, modelId ?? MetricConstants.MetricsModelUnknownLabelValue),
            (MetricConstants.MetricsModelKeyLabelName, modelKey.Value),
            (MetricConstants.MetricsResultLabelName, TurnOutcomeMetric.LabelValue(outcome)));

    // What is known of the turn's row so far; a turn that ends early may never
    // learn the rest.
    private sealed class TurnRow(Guid? existingChatId)
    {
        public Guid? ExistingChatId { get; } = existingChatId;

        // Null until sign-in is confirmed.
        public string? OwnerId { get; set; }

        // Null until the turn's row is written.
        public InsertedTurn? Inserted { get; set; }

        // For logs: the chat the turn is in, as far as it is known yet.
        public Guid? KnownChatId => Inserted?.ChatId ?? ExistingChatId;
    }

    // Where the turn's row is, once it is written.
    private sealed record InsertedTurn(string OwnerId, Guid ChatId);
}
