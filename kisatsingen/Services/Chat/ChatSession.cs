using kisatsingen.Constants;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using Microsoft.Extensions.AI;
using Microsoft.JSInterop;
using Vestfold.Extensions.Metrics.Services;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Services.Chat;

public sealed class ChatSession : IAsyncDisposable
{
    private const string DefaultSystemPrompt = "You are a concise, helpful assistant. Use tools when they help.";

    private static readonly string MetricPrefix = $"{MetricConstants.MetricsAppPrefix}_ChatSession";

    private readonly IAuthenticationService _authenticationService;
    private readonly IChatModelCatalog _catalog;
    private readonly IChatRepository _repo;
    private readonly ChatManager _chatManager;
    private readonly IMetricsService _metrics;
    private readonly ChatClientChannel _channel;
    private readonly TurnStreamer _streamer;
    private readonly ILogger<ChatSession> _logger;

    private readonly List<TranscriptEntry> _entries = [];
    private Guid? _streamingId;

    // Identity only; ChatManager owns the chat's metadata.
    private Guid? _currentChatId;
    private string _effectiveSystemPrompt = DefaultSystemPrompt;

    // Snapshotted by SendAsync, so switching mid-stream only affects the next turn.
    private ChatModel _selectedModel;

    // What PendingModel compares the selection against.
    private ChatModel? _lastAnsweredModel;

    // Null between turns, which is what makes a late Cancel() a harmless no-op.
    private TurnCancellation? _turnCancellation;

    private Task _turnCompletion = Task.CompletedTask;

    // Bumped whenever the view switches chat. Anything that awaited across a bump
    // no longer owns the view and must leave it alone.
    private int _viewVersion;

    public event Action? StateChanged;

    public ChatSession(
        IAuthenticationService authenticationService,
        IChatModelCatalog catalog,
        IChatRepository repo,
        ChatManager chatManager,
        IMetricsService metrics,
        IJSRuntime js,
        ILogger<ChatSession> logger)
    {
        _authenticationService = authenticationService;
        _catalog = catalog;
        _selectedModel = catalog.Default;
        _repo = repo;
        _chatManager = chatManager;
        _metrics = metrics;
        _logger = logger;

        _channel = new ChatClientChannel(js, logger);
        _streamer = new TurnStreamer(_channel, metrics, MetricPrefix);
    }

    public Guid? ChatId => _currentChatId;
    public bool IsBusy { get; private set; }
    public bool HasVisibleMessages => _entries.Count > 0 || _streamingId is not null;

    public Guid? StreamingId => _streamingId;

    public ChatModel SelectedModel => _selectedModel;

    // A switch shows nothing until a turn runs on it, so the composer says so.
    // Null before the first answer: there is no previous model to contrast with.
    public ChatModel? PendingModel =>
        _lastAnsweredModel is { } answered && answered.Key != _selectedModel.Key
            ? _selectedModel
            : null;

    // The allow-list. Every user gets every model today; per-user gating goes here.
    public IReadOnlyList<ChatModel> AvailableModels => _catalog.Models;

    // The key comes from the browser, so it is checked against the allow-list
    // rather than the catalogue — otherwise gating a model later has a way around.
    public Task SelectModelAsync(ChatModelKey key)
    {
        var model = AvailableModels.FirstOrDefault(candidate => candidate.Key == key);

        if (model is null)
        {
            _logger.LogWarning(
                "Refused to switch chat {ChatId} to model {ModelKey}: not in this user's available models.",
                _currentChatId,
                key);
            return Task.CompletedTask;
        }

        if (model.Key == _selectedModel.Key)
        {
            return Task.CompletedTask;
        }

        // State only, no I/O: that is what makes switching mid-stream safe.
        _selectedModel = model;
        Notify();
        return Task.CompletedTask;
    }

    public IReadOnlyList<ChatItemView> Committed => TranscriptProjection.Build(_entries);

    // Estimated from the request we would send, not read from reported usage.
    // Usage double-counts tool turns (FunctionInvokingChatClient sums every round
    // trip), describes only the last answered turn, and is missing after a stop.
    // ConversationUsage keeps real usage: right for cost, wrong for size.
    public long? EstimatedContextTokens
    {
        get
        {
            var request = TranscriptRequest.Build(_entries);

            return request.Count == 0
                ? null
                : ContextTokenEstimator.Estimate(request, _effectiveSystemPrompt);
        }
    }

    public MessageUsage? ConversationUsage
    {
        get
        {
            long input = 0;
            long output = 0;
            long total = 0;
            var hasAny = false;
            foreach (var entry in _entries)
            {
                if (entry is not MessageEntry { Metadata.Usage: { } usage })
                {
                    continue;
                }

                hasAny = true;
                input += usage.InputTokens ?? 0;
                output += usage.OutputTokens ?? 0;
                total += usage.TotalTokens ?? 0;
            }

            return hasAny ? new MessageUsage(input, output, total) : null;
        }
    }

    public async Task LoadAsync(Guid? chatId, CancellationToken ct = default)
    {
        var viewVersion = ++_viewVersion;

        // Leaving a chat stops its turn, and the turn winds down into its own chat
        // before this one is shown.
        await StopTurnForLeaveAsync();
        if (viewVersion != _viewVersion)
        {
            return;
        }

        ClearView();

        if (chatId is Guid id)
        {
            var userObjectId = await _authenticationService.RequireUserObjectIdentifierAsync();
            var chat = await _repo.GetChatAsync(userObjectId, id, ct);

            // A later navigation that finished first must not be overwritten.
            if (viewVersion != _viewVersion)
            {
                return;
            }

            if (chat is not null)
            {
                _currentChatId = chat.Id;
                _effectiveSystemPrompt = chat.SystemPrompt ?? DefaultSystemPrompt;
                _entries.AddRange(TranscriptRestore.Build(chat.Messages, chat.Events, _effectiveSystemPrompt, ResolveModelName, _logger));

                // Continuing a chat must not silently change who answers it.
                _lastAnsweredModel = FindLastAnsweredModel(chat.Messages);
                _selectedModel = _lastAnsweredModel ?? _catalog.Default;
            }
        }

        Notify();
    }

    // Nothing answered, rows older than the picker, and a model since removed all
    // return null: to every caller they mean "no previous model".
    private ChatModel? FindLastAnsweredModel(IReadOnlyList<Data.Entities.ChatMessage> messages)
    {
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            if (ChatModelKey.TryCreate(messages[index].ModelKey) is not { } storedKey)
            {
                continue;
            }

            if (_catalog.TryGet(storedKey, out var model))
            {
                return model;
            }

            _logger.LogInformation(
                "Chat {ChatId} was last answered by model {ModelKey}, which is no longer in the catalogue. Falling back to {DefaultModelKey}.",
                _currentChatId,
                storedKey,
                _catalog.Default.Key);

            return null;
        }

        return null;
    }

    // A key can outlive its model; the raw key still beats a blank or wrong name.
    private string ResolveModelName(ChatModelKey key) =>
        _catalog.TryGet(key, out var model) ? model.DisplayName : key.Value;

    // For when the open chat was just deleted: there is nothing to load.
    public void Reset()
    {
        _viewVersion++;
        ClearView();
        Notify();
    }

    // A turn stops when the session is asked to show another chat, never when a
    // route without a chat is opened: there it finishes and is waiting on return.
    public Task StopTurnForLeaveAsync()
    {
        _turnCancellation?.CancelForLeave();
        return _turnCompletion;
    }

    private void ClearView()
    {
        _entries.Clear();
        _streamingId = null;
        _currentChatId = null;
        _effectiveSystemPrompt = DefaultSystemPrompt;
        _selectedModel = _catalog.Default;
        _lastAnsweredModel = null;
    }

    private bool OwnsView(TurnBinding binding) => binding.ViewVersion == _viewVersion;

    public async Task SendAsync(string text)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        IsBusy = true;

        // The branches below read this local, never the field: a racing
        // DisposeAsync nulls the field mid-turn.
        var turn = new TurnCancellation();
        _turnCancellation = turn;

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _turnCompletion = completion.Task;

        // The turn writes to this chat even after the view has moved on.
        var binding = new TurnBinding(_viewVersion, _currentChatId);

        string? userObjectId = null;

        var stage = TurnStage.Authenticating;

        // A turn that started on one model and prompt must finish on them.
        var modelForThisTurn = _selectedModel;
        var systemPromptForThisTurn = _effectiveSystemPrompt;

        // Outside the try so a stopped or failed turn can still keep what it produced.
        var progress = new TurnProgress();

        // Counted once, in the finally, so every send lands on exactly one outcome.
        // Starts as Failed so a branch that forgets to set it lands in the alerted bucket.
        var outcome = TurnOutcome.Failed;

        // Success is labelled with the model the provider served; the rest with the one requested.
        string? servedModelId = null;

        try
        {
            userObjectId = await _authenticationService.RequireUserObjectIdentifierAsync();

            stage = TurnStage.SavingMessage;
            await PersistUserTurnAsync(binding, userObjectId, text.Trim(), systemPromptForThisTurn, turn.Token);

            stage = TurnStage.Generating;
            await StreamAssistantResponseAsync(binding.StreamId, modelForThisTurn, systemPromptForThisTurn, progress, turn.Token);

            stage = TurnStage.SavingResponse;
            var response = progress.ToResponse();
            await PersistTurnMessagesAsync(binding, userObjectId, response, modelForThisTurn, progress, systemPromptForThisTurn, turn.Token);

            // Only after the write: a turn is a success once its answer is stored.
            servedModelId = response.ModelId;
            outcome = TurnOutcome.Success;
        }
        // The filter is load-bearing: a provider timeout is also an
        // OperationCanceledException, and without it outages would be recorded as
        // the user pressing stop — invisible to failure alerts.
        catch (OperationCanceledException) when (turn.IsCancelled)
        {
            (outcome, var eventKind) = turn switch
            {
                { IsDisconnect: true } => (TurnOutcome.Disconnected, ChatEventKind.Disconnected),
                { IsLeave: true } => (TurnOutcome.LeftChat, ChatEventKind.LeftChat),
                _ => (TurnOutcome.Stopped, ChatEventKind.Stopped)
            };

            // Partial first, so the transcript reads "what it said, then why it stopped".
            await PersistPartialTurnAsync(binding, userObjectId, progress, stage, modelForThisTurn, systemPromptForThisTurn);
            await RecordTurnEventAsync(binding, userObjectId, eventKind);
        }
        // Rethrown: only the error boundary can send the user to sign in.
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
            await PersistPartialTurnAsync(binding, userObjectId, progress, stage, modelForThisTurn, systemPromptForThisTurn);
            await HandleTurnFailureAsync(binding, ex, stage, userObjectId);
        }
        finally
        {
            // Nested so a throwing StateChanged subscriber can't leave LoadAsync
            // waiting on this turn forever.
            try
            {
                // Always, even for a view that moved on: the browser holds a buffer per stream.
                _ = _channel.StreamEnd(binding.StreamId);
                if (OwnsView(binding))
                {
                    _streamingId = null;
                }
                IsBusy = false;

                // Nulled before disposing, so a Cancel() arriving now no-ops instead of
                // reaching disposed sources.
                _turnCancellation = null;
                turn.Dispose();

                Notify();

                // Last and guarded: Prometheus throws on a label mismatch. Earlier, that
                // would skip the teardown and lock the composer; unguarded, it would mask
                // the exception already leaving.
                try
                {
                    CountSend(outcome, servedModelId ?? modelForThisTurn.ModelId, modelForThisTurn.Key);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not count the {Outcome} outcome for chat {ChatId}.", outcome, binding.ChatId);
                }
            }
            finally
            {
                completion.SetResult();
            }
        }
    }

    // One place builds the labels because Prometheus fixes a metric's label names
    // on first use and throws if a later call differs.
    private void CountSend(TurnOutcome outcome, string? modelId, ChatModelKey modelKey) =>
        _metrics.Count(
            $"{MetricPrefix}_Send",
            "Chat send attempts, by outcome",
            (MetricConstants.MetricsModelLabelName, modelId ?? MetricConstants.MetricsModelUnknownLabelValue),
            (MetricConstants.MetricsModelKeyLabelName, modelKey.Value),
            (MetricConstants.MetricsResultLabelName, TurnOutcomeMetric.LabelValue(outcome)));

    // Swallows by design: the turn is lost, the chat is not. The exception is
    // logged here and never shown or persisted.
    private async Task HandleTurnFailureAsync(TurnBinding binding, Exception ex, TurnStage stage, string? userObjectId)
    {
        _logger.LogError(ex, "Chat turn failed during {Stage} for chat {ChatId}", stage, binding.ChatId);

        _metrics.Count(
            $"{MetricPrefix}_Failure",
            "Failed chat turns, by stage and exception type",
            (MetricConstants.MetricsStageLabelName, stage.ToString()),
            (MetricConstants.MetricsExceptionLabelName, ex.GetType().Name));

        await RecordTurnEventAsync(binding, userObjectId, ChatEventKind.Failed, TurnStageNotice.Describe(stage));
    }

    // The entry is added even when there is no chat row to write to yet: a turn
    // that fails that early is when the user most needs to see why.
    private async Task RecordTurnEventAsync(TurnBinding binding, string? userObjectId, ChatEventKind kind, string? detail = null)
    {
        if (OwnsView(binding))
        {
            _entries.Add(new EventEntry(Guid.NewGuid(), kind, detail, DateTimeOffset.UtcNow));
        }

        if (userObjectId is null || binding.ChatId is not Guid chatId)
        {
            return;
        }

        var chatEvent = new Data.Entities.ChatEvent { Kind = kind, Detail = detail };

        try
        {
            // CancellationToken.None, because the turn's own may already be
            // cancelled and would abort recording that very cancellation.
            await _repo.AppendEventAsync(userObjectId, chatId, chatEvent, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Must not replace the outcome it was recording.
            _logger.LogWarning(ex, "Could not persist {Kind} event for chat {ChatId}", kind, chatId);
        }
    }

    private async Task PersistUserTurnAsync(
        TurnBinding binding,
        string userObjectId,
        string text,
        string systemPromptForThisTurn,
        CancellationToken ct)
    {
        // Stopped while authenticating: nothing was shown or saved yet, so keep it that way.
        ct.ThrowIfCancellationRequested();

        var userMessage = new ChatMessage(ChatRole.User, text);
        _entries.Add(new MessageEntry(Guid.NewGuid(), userMessage, null));
        _streamingId = binding.StreamId;

        // Render first: it puts the element the stream appends to in the DOM.
        Notify();
        _ = _channel.StreamStart(binding.StreamId);

        var chatId = await _chatManager.EnsurePersistedAsync(binding.ChatId, text, ct);
        binding.ChatId = chatId;
        if (OwnsView(binding))
        {
            _currentChatId = chatId;
        }

        var entity = ChatMessageMapper.ToEntity(userMessage, systemPromptForThisTurn);
        await _repo.AppendMessagesAsync(userObjectId, chatId, [entity], ct);
    }

    private Task StreamAssistantResponseAsync(
        Guid streamId,
        ChatModel modelForThisTurn,
        string systemPromptForThisTurn,
        TurnProgress progress,
        CancellationToken ct)
    {
        var request = TranscriptRequest.Build(_entries);
        var runtime = _catalog.Resolve(modelForThisTurn.Key);

        // As Instructions rather than a message (see TranscriptRequest), and the same
        // snapshot the turn is persisted with, so the record can't drift from what was sent.
        var options = runtime.CreateOptions();
        options.Instructions = systemPromptForThisTurn;

        return _streamer.StreamAsync(runtime.Client, request, options, streamId, progress, ct);
    }

    // Keeps what a stopped or failed turn had already said, which the user was
    // reading. Only from Generating: earlier nothing was requested, later the turn
    // is already written. Swallows failures so a lost partial can't also cost the
    // user the notice of why the turn ended.
    private async Task PersistPartialTurnAsync(
        TurnBinding binding,
        string? userObjectId,
        TurnProgress progress,
        TurnStage stage,
        ChatModel modelForThisTurn,
        string systemPromptForThisTurn)
    {
        if (stage != TurnStage.Generating || userObjectId is null || binding.ChatId is null || !progress.HasUpdates)
        {
            return;
        }

        // A raw partial can leave history a provider rejects forever; see PartialTurn.
        if (PartialTurn.Prune(progress.ToResponse()) is not { } partial)
        {
            return;
        }

        try
        {
            // CancellationToken.None, because after a stop the turn's own is
            // cancelled and would abort the write that keeps the answer.
            await PersistTurnMessagesAsync(binding, userObjectId, partial, modelForThisTurn, progress, systemPromptForThisTurn, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not save the partial answer for chat {ChatId}. The turn's outcome is still recorded.",
                binding.ChatId);
        }
    }

    // Counts nothing: only SendAsync knows how the turn ended.
    private async Task PersistTurnMessagesAsync(
        TurnBinding binding,
        string userObjectId,
        ChatResponse response,
        ChatModel modelForThisTurn,
        TurnProgress progress,
        string systemPromptForThisTurn,
        CancellationToken ct)
    {
        var lastAssistant = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant);
        var now = DateTimeOffset.UtcNow;
        var isViewOwner = OwnsView(binding);
        var toPersist = new List<Data.Entities.ChatMessage>(response.Messages.Count);
        foreach (var newMessage in response.Messages)
        {
            var includeUsage = ReferenceEquals(newMessage, lastAssistant);

            TurnMetadata? metadata = null;
            if (newMessage.Role == ChatRole.Assistant)
            {
                var usage = includeUsage && response.Usage is { } u
                    ? new MessageUsage(u.InputTokenCount, u.OutputTokenCount, u.TotalTokenCount)
                    : null;
                metadata = new TurnMetadata(
                    response.ModelId,
                    modelForThisTurn.Key,
                    modelForThisTurn.DisplayName,
                    response.ResponseId,
                    response.FinishReason?.Value,
                    usage,
                    progress.DurationMs,
                    progress.FirstTokenMs,
                    now,
                    systemPromptForThisTurn);
            }

            if (isViewOwner)
            {
                _entries.Add(new MessageEntry(Guid.NewGuid(), newMessage, metadata));
            }
            toPersist.Add(ChatMessageMapper.ToEntity(newMessage, response, modelForThisTurn.Key, progress.DurationMs, progress.FirstTokenMs, includeUsage));
        }

        var chatId = binding.ChatId!.Value;
        await _repo.AppendMessagesAsync(userObjectId, chatId, toPersist, ct);
        _chatManager.MarkTouched(chatId, now);

        // Partials too: the rows now carry this model's key, so a reload will report
        // it as the last to answer, and the live session must agree.
        if (OwnsView(binding))
        {
            _lastAnsweredModel = modelForThisTurn;
        }
    }

    private void Notify() => StateChanged?.Invoke();

    public void Cancel() => _turnCancellation?.CancelForUser();

    // Same effect as Cancel; the reason decides whether the transcript says the
    // user stopped the turn or the connection did.
    public void CancelForDisconnect() => _turnCancellation?.CancelForDisconnect();

    // A transient disconnect Blazor recovers from: delivery pauses, the turn runs on.
    public void PauseDelivery() => _channel.Pause();

    public void ResumeDelivery() => _channel.Resume();

    public async ValueTask DisposeAsync()
    {
        // Circuit teardown is a disconnect, not a stop. Cleared first so a racing
        // Cancel() no-ops.
        var turn = _turnCancellation;
        _turnCancellation = null;

        if (turn is not null)
        {
            await turn.CancelForDisconnectAsync();
            turn.Dispose();
        }
    }

    private sealed class TurnBinding(int viewVersion, Guid? chatId)
    {
        public int ViewVersion { get; } = viewVersion;

        // Null until the first message of a new chat creates its row.
        public Guid? ChatId { get; set; } = chatId;

        public Guid StreamId { get; } = Guid.NewGuid();
    }
}
