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

    private static readonly ChatOptions Options = new()
    {
        Tools = [ChatTools.GetCurrentTimeUtcTool]
    };

    private static readonly string MetricPrefix = $"{MetricConstants.MetricsAppPrefix}_ChatSession";

    private readonly IAuthenticationService _authenticationService;
    private readonly IChatClient _client;
    private readonly IChatRepository _repo;
    private readonly IMetricsService _metrics;
    private readonly ChatClientChannel _channel;
    private readonly ILogger<ChatSession> _logger;

    private readonly List<TranscriptEntry> _entries = [];
    private Guid? _streamingId;
    private Data.Entities.Chat? _currentChat;
    private string _effectiveSystemPrompt = DefaultSystemPrompt;

    // The cancellation state of the turn in flight, or null when there is none.
    // Cancel() and the disconnect callbacks reach the live turn through this;
    // both no-op on a null read, which is what makes a stop arriving between
    // turns harmless.
    private TurnCancellation? _turnCancellation;

    public event Action? StateChanged;

    public ChatSession(
        IAuthenticationService authenticationService,
        IChatClient client,
        IChatRepository repo,
        IMetricsService metrics,
        IJSRuntime js,
        ILogger<ChatSession> logger)
    {
        _authenticationService = authenticationService;
        _client = client;
        _repo = repo;
        _metrics = metrics;
        _logger = logger;

        // A lost circuit means nobody is reading the stream, so the turn producing
        // it should stop. The channel is one of three things that can notice a
        // circuit is gone; all three go through the same entry point so they
        // cannot disagree about what to call it.
        _channel = new ChatClientChannel(js, logger, CancelForDisconnect);
    }

    public Guid? ChatId => _currentChat?.Id;
    public bool IsBusy { get; private set; }
    public bool HasVisibleMessages => _entries.Count > 0 || _streamingId is not null;

    public Guid? StreamingId => _streamingId;

    public IReadOnlyList<ChatItemView> Committed => TranscriptProjection.Build(_entries);

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
        _currentChat = null;
        _effectiveSystemPrompt = DefaultSystemPrompt;

        if (chatId is not null)
        {
            var userObjectId = await _authenticationService.RequireUserObjectIdentifierAsync();
            var chat = await _repo.GetChatAsync(userObjectId, chatId.Value, ct);
            if (chat is not null)
            {
                _currentChat = chat;
                _effectiveSystemPrompt = chat.SystemPrompt ?? DefaultSystemPrompt;
                _entries.AddRange(TranscriptRestore.Build(chat.Messages, chat.Events, _effectiveSystemPrompt, _logger));
            }
        }

        Notify();
    }

    public async Task SendAsync(string text)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        IsBusy = true;

        // A local alias, and the catches read it rather than the field: a racing
        // DisposeAsync that nulls the field cannot then NRE the outcome branch.
        // The field stays set so external Cancel() and the disconnect callbacks
        // can still reach this turn while it runs.
        var turn = new TurnCancellation();
        _turnCancellation = turn;

        // Hoisted so the outcome handlers can record against the right owner.
        // Null means the turn ended before authentication returned, in which case
        // there is no chat to write to — only something to show on screen.
        string? userObjectId = null;

        // Hoisted for the same reason: how far the turn got is known only inside
        // the try and needed only by the catches.
        var stage = TurnStage.Authenticating;

        try
        {
            userObjectId = await _authenticationService.RequireUserObjectIdentifierAsync();
            var systemPromptForThisTurn = _effectiveSystemPrompt;

            stage = TurnStage.SavingMessage;
            var streamId = await PersistUserTurnAsync(userObjectId, text.Trim(), systemPromptForThisTurn, turn.Token);

            stage = TurnStage.Generating;
            var (response, durationMs, firstTokenMs) = await StreamAssistantResponseAsync(streamId, systemPromptForThisTurn, turn.Token);

            stage = TurnStage.SavingResponse;
            await PersistResponseAsync(userObjectId, response, durationMs, firstTokenMs, systemPromptForThisTurn, turn.Token);
        }
        // The filter is what makes this handler mean cancellation. An HTTP timeout
        // inside the provider client surfaces as TaskCanceledException — an
        // OperationCanceledException nobody here asked for — and without the filter
        // a real failure would be recorded as a user pressing stop: invisible to
        // failure alerts and a lie in the transcript. Only cancellation of our own
        // token is cancellation; everything else falls through to the failure
        // handler below, where a timeout belongs.
        catch (OperationCanceledException) when (turn.IsCancelled)
        {
            if (turn.IsDisconnect)
            {
                CountSend(MetricConstants.MetricsResultDisconnectedLabelValue);
                await RecordTurnEventAsync(userObjectId, ChatEventKind.Disconnected);
            }
            else
            {
                CountSend(MetricConstants.MetricsResultCancelledLabelValue);
                await RecordTurnEventAsync(userObjectId, ChatEventKind.Stopped);
            }
        }
        // The one failure that is still allowed to leave this method. There is
        // nothing to recover — no identity means no chat to write an event to and
        // no next turn that would go any better — so it goes up to the error
        // boundary, which can do the only useful thing: send the user to sign in.
        catch (UserNotAuthenticatedException)
        {
            CountSend(MetricConstants.MetricsResultUnauthenticatedLabelValue);
            throw;
        }
        // The other failure allowed out, and for the opposite reason to the one
        // above: not "this user is stuck" but "this process is". An allocation
        // failure says nothing about the turn and everything about the instance
        // serving it, so recording it as a chat problem and inviting a retry is
        // the wrong answer — the retry allocates again and fails the same way.
        //
        // What letting it out buys is narrow and worth stating plainly: Blazor
        // tears the circuit down, which sheds this session and the transcript it
        // was holding. It does not recycle the process; the host stays up and
        // other circuits carry on. It is the .NET norm for allocation failures
        // rather than a recovery strategy.
        //
        // Still counted, because the process does survive to be scraped, and the
        // outcome counter's promise is that every attempt lands in exactly one
        // bucket. It is absent from the Failure counter by design — no Stage or
        // Exception drill-down is worth another allocation here — so those two
        // metrics reconcile everywhere except this one case.
        catch (OutOfMemoryException)
        {
            CountSend(MetricConstants.MetricsResultFailedLabelValue);
            throw;
        }
        // Everything else is caught deliberately, bugs in our own code included.
        // A NullReferenceException from a mapper is not more visible for having
        // taken the circuit down with it — it is already logged whole and already
        // carries its type into the Failure counter, which is what an alert can
        // actually read. Tearing down the transcript on top of that costs the user
        // their conversation and tells no one anything new.
        //
        // The class that must never end up here is control flow dressed as an
        // exception — Blazor's NavigationException above all. There is none inside
        // the try today; navigation happens in the page, after this returns. Keep
        // it that way.
        catch (Exception ex)
        {
            await HandleTurnFailureAsync(ex, stage, userObjectId);
        }
        finally
        {
            if (_streamingId is Guid id)
            {
                _channel.StreamEnd(id);
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

    // Counts attempts rather than deliveries, which is why an unauthenticated
    // caller belongs here too: every press of send lands on exactly one Result,
    // so the outcomes sum to the attempts. That invariant is only as good as the
    // call sites — each one has to sit at a point the turn cannot leave, which for
    // Success means after the last await rather than before it.
    //
    // Prometheus fixes a metric's label names the first time it is used and
    // throws on any later call supplying a different number of them, so every
    // outcome has to report the same names in the same order. Building them here
    // rather than at each call site is what keeps that true.
    private void CountSend(string result, string? modelId = null) =>
        _metrics.Count(
            $"{MetricPrefix}_Send",
            "Chat send attempts, by outcome",
            (MetricConstants.MetricsModelLabelName, modelId ?? MetricConstants.MetricsModelUnknownLabelValue),
            (MetricConstants.MetricsResultLabelName, result));

    // Swallows the exception by design. A turn can fail for reasons that say
    // nothing about the next one — the provider rate-limits, a connection drops,
    // the database is briefly unreachable — and rethrowing would take down the
    // circuit and the whole visible transcript with it over one lost turn. So the
    // turn is lost and the chat is not: the user gets a notice naming what was
    // lost, ops get the exception whole plus a counter, and the composer is usable
    // again the moment this returns.
    //
    // The exception itself is never shown or persisted. It is written once, here,
    // where the log is the only reader.
    private async Task HandleTurnFailureAsync(Exception ex, TurnStage stage, string? userObjectId)
    {
        _logger.LogError(ex, "Chat turn failed during {Stage} for chat {ChatId}", stage, _currentChat?.Id);

        CountSend(MetricConstants.MetricsResultFailedLabelValue);
        _metrics.Count(
            $"{MetricPrefix}_Failure",
            "Failed chat turns, by stage and exception type",
            (MetricConstants.MetricsStageLabelName, stage.ToString()),
            (MetricConstants.MetricsExceptionLabelName, ex.GetType().Name));

        await RecordTurnEventAsync(userObjectId, ChatEventKind.Failed, TurnStageNotice.Describe(stage));
    }

    // The partial response is deliberately discarded — the turn did not finish.
    // What is kept is why it ended, so reloading the chat shows the reason for a
    // turn that produced nothing instead of an unexplained gap: Stopped for a user
    // pressing stop, Disconnected for a lost circuit, Failed for a broken turn.
    //
    // The in-memory entry is added unconditionally, the write is not. A turn can
    // end before there is a chat row to write to, or before we know whose chat it
    // is, and that is precisely the case where the user most needs to be told
    // something happened — going silent because persistence was impossible would
    // lose both halves at once.
    private async Task RecordTurnEventAsync(string? userObjectId, ChatEventKind kind, string? detail = null)
    {
        _entries.Add(new EventEntry(Guid.NewGuid(), kind, detail, DateTimeOffset.UtcNow));

        if (userObjectId is null || _currentChat is null)
        {
            return;
        }

        var chatEvent = new Data.Entities.ChatEvent { Kind = kind, Detail = detail };

        try
        {
            // Deliberately not the turn's cancellation source: it may already be
            // cancelled here, and using it would abort the very write that records
            // how the turn ended.
            await _repo.AppendEventAsync(userObjectId, _currentChat.Id, chatEvent, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Failing to record the event must not replace the outcome it was
            // recording or take down the circuit. The entry is already in memory,
            // so this turn still renders correctly; only a reload of the chat
            // would lose it.
            _logger.LogWarning(ex, "Could not persist {Kind} event for chat {ChatId}", kind, _currentChat.Id);
        }
    }

    // Returns the id of the stream it opened, so the caller cannot reach for a
    // nullable field the sequencing below has already guaranteed is set.
    private async Task<Guid> PersistUserTurnAsync(string userObjectId, string text, string systemPromptForThisTurn, CancellationToken ct)
    {
        var userMessage = new ChatMessage(ChatRole.User, text);
        _entries.Add(new MessageEntry(Guid.NewGuid(), userMessage, null));
        var streamId = Guid.NewGuid();
        _streamingId = streamId;

        // Notify before signalling the client: the render this triggers is what
        // puts the streaming element in the DOM for the append calls to target.
        Notify();
        _channel.StreamStart(streamId);

        _currentChat ??= await _repo.CreateChatAsync(userObjectId, BuildTitle(text), ct);

        var entity = ChatMessageMapper.ToEntity(userMessage, systemPromptForThisTurn);
        await _repo.AppendMessagesAsync(userObjectId, _currentChat.Id, [entity], ct);

        return streamId;
    }

    private async Task<(ChatResponse Response, long DurationMs, long? FirstTokenMs)> StreamAssistantResponseAsync(Guid streamId, string systemPromptForThisTurn, CancellationToken ct)
    {
        var duration = _metrics.Histogram($"{MetricPrefix}_Duration", "Elapsed time for a chat message");

        // Stopwatch, not DateTimeOffset.UtcNow: FlushCadence requires offsets that
        // never go backwards, and a wall clock does exactly that when NTP steps it
        // or the host migrates. A backward step would freeze the visible stream
        // until the clock caught up. It also keeps the reported time-to-first-token
        // free of clock adjustments.
        var elapsed = Stopwatch.StartNew();
        long? firstTokenMs = null;
        var updates = new List<ChatResponseUpdate>();

        // Carries flush state that only means anything against the stopwatch
        // above, so the two share a lifetime.
        var cadence = new FlushCadence();

        var request = TranscriptRequest.Build(_entries, systemPromptForThisTurn);

        await foreach (var update in _client.GetStreamingResponseAsync(request, Options, ct))
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
                _channel.StreamAppend(streamId, due);
            }
        }

        if (cadence.Drain() is { } remaining)
        {
            _channel.StreamAppend(streamId, remaining);
        }

        var response = updates.ToChatResponse();
        var durationMs = (long)duration.ObserveDuration().TotalMilliseconds;
        return (response, durationMs, firstTokenMs);
    }

    private async Task PersistResponseAsync(string userObjectId, ChatResponse response, long durationMs, long? firstTokenMs, string systemPromptForThisTurn, CancellationToken ct)
    {
        var lastAssistant = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant);
        var now = DateTimeOffset.UtcNow;
        var toPersist = new List<Data.Entities.ChatMessage>(response.Messages.Count);
        foreach (var newMessage in response.Messages)
        {
            var includeUsage = ReferenceEquals(newMessage, lastAssistant);

            AssistantMetadata? metadata = null;
            if (newMessage.Role == ChatRole.Assistant)
            {
                var usage = includeUsage && response.Usage is { } u
                    ? new MessageUsage(u.InputTokenCount, u.OutputTokenCount, u.TotalTokenCount)
                    : null;
                metadata = new AssistantMetadata(
                    response.ModelId,
                    response.ResponseId,
                    response.FinishReason?.Value,
                    usage,
                    durationMs,
                    firstTokenMs,
                    now,
                    systemPromptForThisTurn);
            }

            _entries.Add(new MessageEntry(Guid.NewGuid(), newMessage, metadata));
            toPersist.Add(ChatMessageMapper.ToEntity(newMessage, response, durationMs, firstTokenMs, includeUsage));
        }

        await _repo.AppendMessagesAsync(userObjectId, _currentChat!.Id, toPersist, ct);

        // Counted last, after the write that makes the turn real. Counting it on
        // entry instead would let one attempt land on two Results — Success, then
        // Failed or Cancelled from whatever happened in the lines above — which is
        // the one thing the outcome counter promises cannot happen.
        CountSend(MetricConstants.MetricsResultSuccessLabelValue, response.ModelId);
    }

    private static string BuildTitle(string userText)
    {
        var trimmed = userText.Trim();
        return trimmed.Length <= 60 ? trimmed : trimmed[..60].TrimEnd() + "…";
    }

    private void Notify() => StateChanged?.Invoke();

    // The user asked for the turn to end.
    public void Cancel() => _turnCancellation?.CancelForUser();

    // The browser stopped listening. Identical effect on the turn, different
    // reason, and the reason is the entire point: it decides whether the
    // transcript the user comes back to says they stopped the turn or that the
    // connection did. Everything that can notice a dead circuit calls this rather
    // than Cancel, so one event cannot be filed under two names depending on
    // which watcher happened to spot it first.
    public void CancelForDisconnect() => _turnCancellation?.CancelForDisconnect();

    public async ValueTask DisposeAsync()
    {
        // A disconnect, not a stop. This runs when the circuit's scope is torn
        // down: the tab closed, the circuit was evicted, the host is shutting
        // down. Not one of those is a user pressing stop, and recording it as one
        // would tell them they did something they did not.
        // Read into a local and clear the field first, so a Cancel() arriving
        // mid-teardown no-ops instead of reaching sources about to be disposed.
        var turn = _turnCancellation;
        _turnCancellation = null;

        if (turn is not null)
        {
            await turn.CancelForDisconnectAsync();
            turn.Dispose();
        }
    }
}
