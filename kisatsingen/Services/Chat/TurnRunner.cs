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

    // Throws only what the page must handle itself: an unauthenticated caller and
    // an allocation failure.
    public async Task<Turn> RunAsync(
        Turn turn,
        Guid? chatId,
        IReadOnlyList<ChatMessage> request,
        ChatModel model,
        TurnCancellation cancellation,
        TurnObserver observer)
    {
        var builder = new TurnBuilder(turn);
        var stage = TurnStage.Authenticating;
        var row = new TurnRow(chatId);

        // Counted once, in the finally, so every send lands on exactly one outcome.
        // Starts as Failed so a branch that forgets to set it lands in the alerted bucket.
        var outcome = TurnOutcome.Failed;

        // Success is labelled with the model the provider served; the rest with the one requested.
        string? servedModelId = null;

        try
        {
            row.OwnerId = await _authenticationService.RequireUserObjectIdentifierAsync();

            stage = TurnStage.SavingMessage;
            await InsertAsync(row, turn, observer, cancellation.Token);

            stage = TurnStage.Generating;
            await StreamAsync(turn, builder, request, model, observer, cancellation.Token);

            stage = TurnStage.SavingResponse;
            var answered = builder.Finish(TurnStatus.Completed);
            await SaveAsync(row, answered, cancellation.Token);

            // Only after the write: a turn is a success once its answer is stored.
            servedModelId = answered.Metadata?.ServedModelId;
            outcome = TurnOutcome.Success;
            return answered;
        }
        // The filter is load-bearing: a provider timeout is also an
        // OperationCanceledException, and without it outages would be filed as
        // user stops, invisible to failure alerts.
        catch (OperationCanceledException) when (cancellation.IsCancelled)
        {
            (outcome, var status) = cancellation switch
            {
                { IsDisconnect: true } => (TurnOutcome.Disconnected, TurnStatus.Disconnected),
                { IsLeave: true } => (TurnOutcome.LeftChat, TurnStatus.LeftChat),
                _ => (TurnOutcome.Stopped, TurnStatus.Stopped)
            };

            var stopped = builder.Finish(status);
            await SaveEndingAsync(row, stopped);
            return stopped;
        }
        // Rethrown: with no error boundary it ends the circuit, and the reload is what
        // the sign-in middleware redirects. Practically unreachable mid-chat: a
        // circuit's sign-in state is fixed when it starts.
        catch (UserNotAuthenticatedException)
        {
            outcome = TurnOutcome.Unauthenticated;
            throw;
        }
        // Rethrown: an allocation failure isn't this turn's fault and a retry fails
        // the same way. Kept off the Failure counter, so the two don't reconcile here.
        catch (OutOfMemoryException)
        {
            outcome = TurnOutcome.Failed;
            throw;
        }
        // Swallowed, bugs included: they are logged whole, and taking the circuit
        // down would only cost the user their transcript. Nothing that is control
        // flow — Blazor's NavigationException above all — may ever be thrown in the try.
        catch (Exception ex)
        {
            outcome = TurnOutcome.Failed;
            RecordFailure(ex, stage, row.ChatId);

            var failed = builder.Finish(TurnStatus.Failed, stage);

            // Not when the save itself failed: retried, the row would hold the whole
            // answer under a notice saying it was not stored.
            if (stage != TurnStage.SavingResponse)
            {
                await SaveEndingAsync(row, failed);
            }

            return failed;
        }
        finally
        {
            // Guarded: Prometheus throws on a label mismatch, and unguarded that would
            // mask the exception already leaving.
            try
            {
                CountSend(outcome, servedModelId ?? model.ModelId, model.Key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not count the {Outcome} outcome for chat {ChatId}.", outcome, row.ChatId);
            }
        }
    }

    private async Task InsertAsync(TurnRow row, Turn turn, TurnObserver observer, CancellationToken ct)
    {
        // Stopped while authenticating: nothing was saved yet, so keep it that way.
        ct.ThrowIfCancellationRequested();

        var chatId = await _chatManager.EnsurePersistedAsync(row.ChatId, turn.Prompt, ct);
        row.ChatId = chatId;
        observer.ChatPersisted(chatId);

        await _repo.InsertTurnAsync(row.OwnerId!, chatId, ChatTurnMapper.ToEntity(turn), ct);
        row.IsInserted = true;
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
    private async Task SaveEndingAsync(TurnRow row, Turn ended)
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

    private void RecordFailure(Exception ex, TurnStage stage, Guid? chatId)
    {
        _logger.LogError(ex, "Chat turn failed during {Stage} for chat {ChatId}", stage, chatId);

        _metrics.Count(
            $"{_metricPrefix}_Failure",
            "Failed chat turns, by stage and exception type",
            (MetricConstants.MetricsStageLabelName, stage.ToString()),
            (MetricConstants.MetricsExceptionLabelName, ex.GetType().Name));
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
