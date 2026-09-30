using kisatsingen.Constants;
using kisatsingen.Data.Repositories;
using Microsoft.Extensions.AI;
using Vestfold.Extensions.Metrics.Services;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Services.Chat;

// ChatPersisted fires mid-turn so a new chat's id can reach the URL before the answer ends.
internal sealed record TurnObserver(Action<Guid> ChatPersisted, Action<Turn> Changed);

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

    public TurnRunner(
        IAuthenticationService authenticationService,
        IChatModelCatalog catalog,
        IChatRepository repo,
        ChatManager chatManager,
        IMetricsService metrics,
        TurnStreamer streamer,
        string metricPrefix,
        ILogger logger)
    {
        _authenticationService = authenticationService;
        _catalog = catalog;
        _repo = repo;
        _chatManager = chatManager;
        _metrics = metrics;
        _streamer = streamer;
        _metricPrefix = metricPrefix;
        _logger = logger;
    }

    // Throws only what the page must handle itself: an unauthenticated caller, an
    // allocation failure, and a metrics failure, surfaced rather than swallowed.
    public async Task<Turn> RunAsync(
        Turn turn,
        Guid? chatId,
        IReadOnlyList<ChatMessage> request,
        ChatModel model,
        TurnCancellation cancellation,
        TurnObserver observer)
    {
        var row = new TurnRow(chatId);

        // Counted once, in the finally, so every send lands on exactly one outcome.
        // Starts as Failed so an exception nothing classified lands in the alerted bucket.
        var outcome = TurnOutcome.Failed;
        string? servedModelId = null;

        try
        {
            var attempt = await ExecuteAsync(row, turn, request, model, cancellation, observer);
            outcome = attempt.Outcome;
            servedModelId = attempt.ServedModelId;

            await FinishAttemptAsync(row, attempt);
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

    // Decides how the attempt went and does nothing else about it: the catches only
    // name the outcome, so none of them can throw past it.
    private async Task<TurnAttempt> ExecuteAsync(
        TurnRow row,
        Turn turn,
        IReadOnlyList<ChatMessage> request,
        ChatModel model,
        TurnCancellation cancellation,
        TurnObserver observer)
    {
        var builder = new TurnBuilder(turn, ContextTokenEstimator.Estimate(request, turn.SystemPrompt));
        var stage = TurnStage.Authenticating;

        try
        {
            row.OwnerId = await _authenticationService.RequireUserObjectIdentifierAsync();

            stage = TurnStage.SavingMessage;
            await InsertAsync(row, turn, observer, cancellation.Token);

            stage = TurnStage.Generating;
            await StreamAsync(turn, builder, request, model, observer, cancellation.Token);

            // CancellationToken.None: the answer is complete, so a stop or a switch of
            // chat during this one write must not store it as stopped.
            stage = TurnStage.SavingResponse;
            var answered = builder.Finish(TurnStatus.Completed);
            await SaveAsync(row, answered, CancellationToken.None);

            // Only after the write: a turn is a success once its answer is stored.
            return new TurnAttempt(answered, TurnOutcome.Success);
        }
        // The filter is load-bearing: a provider timeout is also an
        // OperationCanceledException, and without it outages would be filed as
        // user stops, invisible to failure alerts.
        catch (OperationCanceledException) when (cancellation.IsCancelled)
        {
            var (outcome, status) = cancellation switch
            {
                { IsDisconnect: true } => (TurnOutcome.Disconnected, TurnStatus.Disconnected),
                { IsLeave: true } => (TurnOutcome.LeftChat, TurnStatus.LeftChat),
                _ => (TurnOutcome.Stopped, TurnStatus.Stopped)
            };

            return new TurnAttempt(builder.Finish(status), outcome);
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
            return new TurnAttempt(builder.Finish(TurnStatus.Failed, stage), TurnOutcome.Failed, ex);
        }
    }

    // Every effect of an attempt; the outcome is decided, and nothing here changes it.
    // The ending is saved before anything that may throw, so a metrics failure that
    // surfaces cannot also leave the stored turn unfinished.
    private async Task FinishAttemptAsync(TurnRow row, TurnAttempt attempt)
    {
        LogFailure(row, attempt);

        if (attempt.ShouldSaveTurn)
        {
            await SaveEndedTurnAsync(row, attempt.Turn);
        }

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
                $"{_metricPrefix}_Failure",
                "Failed chat turns, by stage and exception type",
                (MetricConstants.MetricsStageLabelName, stage.ToString()),
                (MetricConstants.MetricsExceptionLabelName, error.GetType().Name));
        }
    }

    private async Task InsertAsync(TurnRow row, Turn turn, TurnObserver observer, CancellationToken ct)
    {
        // Stopped before anything was saved: keep it that way.
        ct.ThrowIfCancellationRequested();

        var isNewChat = row.ChatId is null;
        var chatId = await _chatManager.EnsurePersistedAsync(row.ChatId, turn.Prompt, ct);

        try
        {
            await _repo.InsertTurnAsync(row.OwnerId!, chatId, ChatTurnMapper.ToEntity(turn), ct);
        }
        // A new chat whose first question did not save would open empty, so it goes.
        // Announced only after this, so neither the URL nor the view ever held it.
        catch (Exception) when (isNewChat)
        {
            await DiscardChatAsync(chatId);
            throw;
        }

        row.ChatId = chatId;
        row.IsInserted = true;
        observer.ChatPersisted(chatId);
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

    private Task StreamAsync(
        Turn turn,
        TurnBuilder builder,
        IReadOnlyList<ChatMessage> request,
        ChatModel model,
        TurnObserver observer,
        CancellationToken ct)
    {
        var runtime = _catalog.Resolve(model.Key);

        // The snapshot the turn is stored with, so the record can't drift from what was sent.
        var options = runtime.CreateOptions();
        options.Instructions = turn.SystemPrompt;

        return _streamer.StreamAsync(runtime.Client, request, options, builder, () => observer.Changed(builder.Snapshot()), ct);
    }

    private async Task SaveAsync(TurnRow row, Turn ended, CancellationToken ct)
    {
        var chatId = row.ChatId!.Value;
        await _repo.UpdateTurnAsync(row.OwnerId!, chatId, ChatTurnMapper.ToEntity(ended), ct);
        _chatManager.MarkTouched(chatId, DateTimeOffset.UtcNow);
    }

    // CancellationToken.None: after a stop the turn's own is cancelled and would
    // abort the write that records it. Swallows failures so a lost stored copy
    // cannot replace the outcome the user is shown.
    private async Task SaveEndedTurnAsync(TurnRow row, Turn ended)
    {
        if (!row.IsInserted)
        {
            return;
        }

        try
        {
            await SaveAsync(row, ended, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not save how turn {TurnId} in chat {ChatId} ended ({Status}). It reads back as unfinished.",
                ended.Id,
                row.ChatId,
                ended.Status);
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

    private sealed class TurnRow(Guid? chatId)
    {
        public string? OwnerId { get; set; }

        // Null until a new chat's first question creates its row.
        public Guid? ChatId { get; set; } = chatId;

        public bool IsInserted { get; set; }
    }
}
