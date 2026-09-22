using System.Diagnostics;
using kisatsingen.AIFunctions;
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
    private readonly ILogger<ChatSession> _logger;

    private readonly List<TranscriptEntry> _entries = [];
    private Guid? _streamingId;

    // The chat this session is working on, or null between chats. ChatManager
    // owns chat metadata (title, timestamps); Session tracks only identity plus
    // the runtime state of the turn in flight.
    private Guid? _currentChatId;
    private string _effectiveSystemPrompt = DefaultSystemPrompt;

    // Which model the next turn will use. Read once at the top of SendAsync, the
    // same way the system prompt is, so switching while a response streams cannot
    // touch the turn in flight — it takes effect on the following one.
    private ChatModel _selectedModel;

    // The model that produced the most recent answer, or null when nothing has
    // answered yet or the model that did is no longer in the catalogue. What the
    // selection is compared against to know a switch is still pending.
    private ChatModel? _lastAnsweredModel;

    // The cancellation state of the turn in flight, or null when there is none.
    // Cancel() and the disconnect callbacks reach the live turn through this;
    // both no-op on a null read, which is what makes a stop arriving between
    // turns harmless.
    private TurnCancellation? _turnCancellation;

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
    }

    public Guid? ChatId => _currentChatId;
    public bool IsBusy { get; private set; }
    public bool HasVisibleMessages => _entries.Count > 0 || _streamingId is not null;

    public Guid? StreamingId => _streamingId;

    public ChatModel SelectedModel => _selectedModel;

    // The model the next turn will use, when that differs from the one that
    // answered last — the composer says so, because a switch produces nothing
    // visible until a turn actually runs on it. Null when there is nothing
    // pending, including on a chat that has not been answered yet: there is no
    // previous model to contrast with, and the picker already reads "Kompleks".
    public ChatModel? PendingModel =>
        _lastAnsweredModel is { } answered && answered.Key != _selectedModel.Key
            ? _selectedModel
            : null;

    // The allow-list. Reads the catalogue directly — no I/O, no auth round-trip
    // — because every signed-in user gets every catalogued model today. When
    // per-user gating lands, this becomes the one place a user context is
    // threaded through (and Default is redesigned alongside it — see
    // IChatModelCatalog).
    public IReadOnlyList<ChatModel> AvailableModels => _catalog.Models;

    // The key arrives from the browser, so it is re-checked against the
    // allow-list rather than looked up in the catalogue directly. That check is
    // meaningless today and has to be here anyway: the moment a model becomes
    // role-gated, every path that skipped it becomes the way around it.
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

        // No write of any kind: the switch is state, and the transcript derives the
        // boundary from the ModelKey the turns either side of it record. Keeping
        // this free of I/O is what makes switching mid-stream safe — there is
        // nothing to order against a turn that is still running.
        _selectedModel = model;
        Notify();
        return Task.CompletedTask;
    }

    public IReadOnlyList<ChatItemView> Committed => TranscriptProjection.Build(_entries);

    // How much context the next request will carry, as the last turn measured it.
    // That turn's input already contained the whole history, so its input plus its
    // own output is the conversation's current size — before the user has typed
    // anything, which only adds to it.
    //
    // Deliberately not ConversationUsage below. That one sums every turn, which is
    // the right answer to "what has this conversation cost" and the wrong one here:
    // each turn's input re-counts the whole history, so the total grows roughly
    // with the square of the turn count and would warn about a 12k chat at 128k.
    //
    // Null when nothing has reported usage. A missing number is not zero, and a
    // context warning must not be derived from one.
    public long? EstimatedContextTokens
    {
        get
        {
            for (var index = _entries.Count - 1; index >= 0; index--)
            {
                if (_entries[index] is not MessageEntry { Metadata.Usage: { } usage })
                {
                    continue;
                }

                // Output counts: it is part of the history the next request sends.
                return usage.InputTokens is { } input
                    ? input + (usage.OutputTokens ?? 0)
                    : null;
            }

            return null;
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
        _entries.Clear();
        _streamingId = null;
        _currentChatId = null;
        _effectiveSystemPrompt = DefaultSystemPrompt;

        _selectedModel = _catalog.Default;
        _lastAnsweredModel = null;

        if (chatId is Guid id)
        {
            var userObjectId = await _authenticationService.RequireUserObjectIdentifierAsync();
            var chat = await _repo.GetChatAsync(userObjectId, id, ct);
            if (chat is not null)
            {
                _currentChatId = chat.Id;
                _effectiveSystemPrompt = chat.SystemPrompt ?? DefaultSystemPrompt;
                _entries.AddRange(TranscriptRestore.Build(chat.Messages, chat.Events, _effectiveSystemPrompt, ResolveModelName, _logger));

                // Reopening resumes on whatever answered last, so continuing a
                // conversation does not silently change who is answering it.
                _lastAnsweredModel = FindLastAnsweredModel(chat.Messages);
                _selectedModel = _lastAnsweredModel ?? _catalog.Default;
            }
        }

        Notify();
    }

    // Null when nothing has answered yet, when the rows predate the picker, or
    // when the model that answered has since left the catalogue. All three mean
    // the same thing to every caller: there is no previous model to resume or to
    // contrast the selection against.
    private ChatModel? FindLastAnsweredModel(IReadOnlyList<Data.Entities.ChatMessage> messages)
    {
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            // A blank key is read as no key at all, the same as null: the column
            // permits it, and a row that names no model must not cost the user the
            // chat. See ChatModelKey.TryCreate.
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

    // A key can outlive the model it named, so this always answers. The key itself
    // is the fallback: it is at least what the row says, which beats an empty
    // divider or the name of a different model.
    private string ResolveModelName(ChatModelKey key) =>
        _catalog.TryGet(key, out var model) ? model.DisplayName : key.Value;

    // Sync clear for when the currently-open chat has just been deleted out
    // from under this session — there is nothing to load, so callers do not
    // need to route back through LoadAsync.
    public void Reset()
    {
        _entries.Clear();
        _streamingId = null;
        _currentChatId = null;
        _effectiveSystemPrompt = DefaultSystemPrompt;
        _selectedModel = _catalog.Default;
        _lastAnsweredModel = null;
        Notify();
    }

    public async Task SendAsync(string text)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        IsBusy = true;

        // The catches read the local, not the field: a racing DisposeAsync nulls
        // the field and would NRE the outcome branch. The field stays set so
        // Cancel() and the disconnect callbacks can reach this turn while it runs.
        var turn = new TurnCancellation();
        _turnCancellation = turn;

        // Null means the turn ended before authentication returned: no chat to
        // write to, only something to show on screen.
        string? userObjectId = null;

        var stage = TurnStage.Authenticating;

        // Captured before the try so every catch can label the outcome metric
        // with the model whose turn actually failed. Same snapshot pattern the
        // system prompt uses below — both may change while this turn runs, and
        // a turn that started on one model must finish on it.
        var modelForThisTurn = _selectedModel;

        try
        {
            userObjectId = await _authenticationService.RequireUserObjectIdentifierAsync();
            var systemPromptForThisTurn = _effectiveSystemPrompt;

            stage = TurnStage.SavingMessage;
            var streamId = await PersistUserTurnAsync(userObjectId, text.Trim(), systemPromptForThisTurn, turn.Token);

            stage = TurnStage.Generating;
            var (response, durationMs, firstTokenMs) = await StreamAssistantResponseAsync(streamId, modelForThisTurn, systemPromptForThisTurn, turn.Token);

            stage = TurnStage.SavingResponse;
            await PersistResponseAsync(userObjectId, response, modelForThisTurn, durationMs, firstTokenMs, systemPromptForThisTurn, turn.Token);
        }
        // The filter is load-bearing: a provider HTTP timeout arrives as
        // TaskCanceledException, an OperationCanceledException nobody here asked
        // for. Without it, outages are recorded as the user pressing stop —
        // invisible to failure alerts. Only our own cancellation is cancellation.
        catch (OperationCanceledException) when (turn.IsCancelled)
        {
            if (turn.IsDisconnect)
            {
                CountSend(MetricConstants.MetricsResultDisconnectedLabelValue, modelForThisTurn.ModelId, modelForThisTurn.Key);
                await RecordTurnEventAsync(userObjectId, ChatEventKind.Disconnected);
            }
            else
            {
                CountSend(MetricConstants.MetricsResultCancelledLabelValue, modelForThisTurn.ModelId, modelForThisTurn.Key);
                await RecordTurnEventAsync(userObjectId, ChatEventKind.Stopped);
            }
        }
        // Allowed out: no identity means no chat to record an event against, and
        // only the error boundary above can do the useful thing and send the user
        // to sign in.
        catch (UserNotAuthenticatedException)
        {
            CountSend(MetricConstants.MetricsResultUnauthenticatedLabelValue, modelForThisTurn.ModelId, modelForThisTurn.Key);
            throw;
        }
        // Not swallowed: an allocation failure says nothing about this turn, and a
        // retry allocates again and fails the same way. Letting it out sheds the
        // circuit, not the process. Counted so every attempt still lands in exactly
        // one Result bucket; deliberately absent from the Failure counter, the one
        // place those two metrics do not reconcile.
        catch (OutOfMemoryException)
        {
            CountSend(MetricConstants.MetricsResultFailedLabelValue, modelForThisTurn.ModelId, modelForThisTurn.Key);
            throw;
        }
        // Catches our own bugs too: they are already logged whole and carry their
        // type into the Failure counter, so taking the circuit down as well only
        // costs the user their transcript.
        //
        // What must never reach here is control flow dressed as an exception —
        // Blazor's NavigationException above all. There is none inside the try
        // today; navigation happens in the page, after this returns. Keep it so.
        catch (Exception ex)
        {
            await HandleTurnFailureAsync(ex, stage, userObjectId, modelForThisTurn);
        }
        finally
        {
            if (_streamingId is Guid id)
            {
                _ = _channel.StreamEnd(id);
            }
            _streamingId = null;
            IsBusy = false;

            // Null the field first so a Cancel() or disconnect callback arriving
            // during teardown reads null and no-ops, rather than racing Cancel on
            // sources that are about to be disposed.
            _turnCancellation = null;
            turn.Dispose();

            Notify();
        }
    }

    // Every press of send lands on exactly one Result, so the outcomes sum to the
    // attempts. That holds only if each call site sits where the turn cannot leave
    // — for Success, after the last await rather than before it.
    //
    // Prometheus fixes a metric's label names on first use and throws if a later
    // call supplies a different number, so every outcome must report the same
    // names in the same order. Building them here is what guarantees that.
    private void CountSend(string result, string? modelId = null, ChatModelKey? modelKey = null) =>
        _metrics.Count(
            $"{MetricPrefix}_Send",
            "Chat send attempts, by outcome",
            (MetricConstants.MetricsModelLabelName, modelId ?? MetricConstants.MetricsModelUnknownLabelValue),
            (MetricConstants.MetricsModelKeyLabelName, modelKey?.Value ?? MetricConstants.MetricsModelUnknownLabelValue),
            (MetricConstants.MetricsResultLabelName, result));

    // Swallows by design: the turn is lost, the chat is not. The exception is
    // written here and only here — never shown, never persisted.
    private async Task HandleTurnFailureAsync(Exception ex, TurnStage stage, string? userObjectId, ChatModel modelForThisTurn)
    {
        _logger.LogError(ex, "Chat turn failed during {Stage} for chat {ChatId}", stage, _currentChatId);

        CountSend(MetricConstants.MetricsResultFailedLabelValue, modelForThisTurn.ModelId, modelForThisTurn.Key);
        _metrics.Count(
            $"{MetricPrefix}_Failure",
            "Failed chat turns, by stage and exception type",
            (MetricConstants.MetricsStageLabelName, stage.ToString()),
            (MetricConstants.MetricsExceptionLabelName, ex.GetType().Name));

        await RecordTurnEventAsync(userObjectId, ChatEventKind.Failed, TurnStageNotice.Describe(stage));
    }

    // The partial response is discarded; why the turn ended is kept, so a reload
    // explains the gap instead of showing an unanswered message.
    //
    // The in-memory entry is added unconditionally, the write is not: a turn can
    // end before there is a chat row to write to, and that is exactly when the
    // user most needs to see something on screen.
    private async Task RecordTurnEventAsync(string? userObjectId, ChatEventKind kind, string? detail = null)
    {
        _entries.Add(new EventEntry(Guid.NewGuid(), kind, detail, DateTimeOffset.UtcNow));

        if (userObjectId is null || _currentChatId is not Guid chatId)
        {
            return;
        }

        var chatEvent = new Data.Entities.ChatEvent { Kind = kind, Detail = detail };

        try
        {
            // Not the turn's own source — it may already be cancelled, and would
            // abort the very write recording that cancellation.
            await _repo.AppendEventAsync(userObjectId, chatId, chatEvent, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Must not replace the outcome it was recording. The entry is already
            // in memory, so only a reload would lose it.
            _logger.LogWarning(ex, "Could not persist {Kind} event for chat {ChatId}", kind, chatId);
        }
    }

    // Returns the stream id rather than leaving the caller to read the nullable
    // field this already guaranteed is set.
    private async Task<Guid> PersistUserTurnAsync(string userObjectId, string text, string systemPromptForThisTurn, CancellationToken ct)
    {
        var userMessage = new ChatMessage(ChatRole.User, text);
        _entries.Add(new MessageEntry(Guid.NewGuid(), userMessage, null));
        var streamId = Guid.NewGuid();
        _streamingId = streamId;

        // Notify before signalling the client: the render this triggers is what
        // puts the streaming element in the DOM for the append calls to target.
        Notify();
        _ = _channel.StreamStart(streamId);

        var chatId = await _chatManager.EnsurePersistedAsync(_currentChatId, text, ct);
        _currentChatId = chatId;

        var entity = ChatMessageMapper.ToEntity(userMessage, systemPromptForThisTurn);
        await _repo.AppendMessagesAsync(userObjectId, chatId, [entity], ct);

        return streamId;
    }

    private async Task<(ChatResponse Response, long DurationMs, long? FirstTokenMs)> StreamAssistantResponseAsync(
        Guid streamId,
        ChatModel modelForThisTurn,
        string systemPromptForThisTurn,
        CancellationToken ct)
    {
        var duration = _metrics.Histogram($"{MetricPrefix}_Duration", "Elapsed time for a chat message");

        // Stopwatch, not UtcNow: FlushCadence needs offsets that never go
        // backwards, and a wall clock does when NTP steps it — which would freeze
        // the visible stream until the clock caught up.
        var elapsed = Stopwatch.StartNew();
        long? firstTokenMs = null;
        var updates = new List<ChatResponseUpdate>();
        var cadence = new FlushCadence();

        var request = TranscriptRequest.Build(_entries);

        var runtime = _catalog.Resolve(modelForThisTurn.Key);

        // The system prompt rides on the options rather than the message list; see
        // TranscriptRequest. Same snapshot the turn is persisted with, so what the
        // model was told and what the transcript records can never drift apart.
        var options = runtime.CreateOptions();
        options.Instructions = systemPromptForThisTurn;

        await foreach (var update in runtime.Client.GetStreamingResponseAsync(request, options, ct))
        {
            updates.Add(update);
            var offsetMs = elapsed.ElapsedMilliseconds;

            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case FunctionCallContent call:
                        _metrics.Count($"{MetricPrefix}_ToolCall", "Number of tool calls performed", ("Tool", call.Name));
                        break;
                    case FunctionResultContent:
                        _metrics.Count($"{MetricPrefix}_ToolResult", "Number of tool results retrieved");
                        break;
                }
            }

            if (string.IsNullOrEmpty(update.Text))
            {
                continue;
            }

            firstTokenMs ??= offsetMs;

            if (cadence.Append(offsetMs, update.Text) is { } due)
            {
                _ = _channel.StreamAppend(streamId, due);
            }
        }

        if (cadence.Drain() is { } remaining)
        {
            _ = _channel.StreamAppend(streamId, remaining);
        }

        var response = updates.ToChatResponse();
        var durationMs = (long)duration.ObserveDuration().TotalMilliseconds;
        return (response, durationMs, firstTokenMs);
    }

    private async Task PersistResponseAsync(
        string userObjectId,
        ChatResponse response,
        ChatModel modelForThisTurn,
        long durationMs,
        long? firstTokenMs,
        string systemPromptForThisTurn,
        CancellationToken ct)
    {
        var lastAssistant = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant);
        var now = DateTimeOffset.UtcNow;
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
                    durationMs,
                    firstTokenMs,
                    now,
                    systemPromptForThisTurn);
            }

            _entries.Add(new MessageEntry(Guid.NewGuid(), newMessage, metadata));
            toPersist.Add(ChatMessageMapper.ToEntity(newMessage, response, modelForThisTurn.Key, durationMs, firstTokenMs, includeUsage));
        }

        var chatId = _currentChatId!.Value;
        await _repo.AppendMessagesAsync(userObjectId, chatId, toPersist, ct);
        _chatManager.MarkTouched(chatId, now);

        // Set only once the answer is saved. A turn that broke did not answer, so
        // a pending switch stays pending rather than being marked as taken effect.
        _lastAnsweredModel = modelForThisTurn;

        // Counted last, after the write that makes the turn real. Counting it on
        // entry instead would let one attempt land on two Results — Success, then
        // Failed or Cancelled from whatever happened in the lines above — which is
        // the one thing the outcome counter promises cannot happen.
        CountSend(MetricConstants.MetricsResultSuccessLabelValue, response.ModelId, modelForThisTurn.Key);
    }

    private void Notify() => StateChanged?.Invoke();

    // The user asked for the turn to end.
    public void Cancel() => _turnCancellation?.CancelForUser();

    // The circuit is gone for good — not merely disconnected, which Blazor
    // recovers from. Same effect on the turn as Cancel, different reason, and the
    // reason decides whether the reloaded transcript says the user stopped the
    // turn or the connection did.
    public void CancelForDisconnect() => _turnCancellation?.CancelForDisconnect();

    // The transport went away or came back on a circuit Blazor is retaining. The
    // turn is unaffected either way — only delivery to the browser pauses, which
    // is why neither of these touches cancellation.
    public void PauseDelivery() => _channel.Pause();

    public void ResumeDelivery() => _channel.Resume();

    public async ValueTask DisposeAsync()
    {
        // A disconnect, not a stop: this runs on circuit teardown — tab closed,
        // circuit evicted, host shutting down — none of which is a user pressing
        // stop. Cleared first so a Cancel() racing teardown no-ops rather than
        // reaching sources about to be disposed.
        var turn = _turnCancellation;
        _turnCancellation = null;

        if (turn is not null)
        {
            await turn.CancelForDisconnectAsync();
            turn.Dispose();
        }
    }
}
