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

        // The catches read the local, not the field: a racing DisposeAsync nulls
        // the field and would NRE the outcome branch. The field stays set so
        // Cancel() and the disconnect callbacks can reach this turn while it runs.
        var turn = new TurnCancellation();
        _turnCancellation = turn;

        // Null means the turn ended before authentication returned: no chat to
        // write to, only something to show on screen.
        string? userObjectId = null;

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
        // The filter is load-bearing: a provider HTTP timeout arrives as
        // TaskCanceledException, an OperationCanceledException nobody here asked
        // for. Without it, outages are recorded as the user pressing stop —
        // invisible to failure alerts. Only our own cancellation is cancellation.
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
        // Allowed out: no identity means no chat to record an event against, and
        // only the error boundary above can do the useful thing and send the user
        // to sign in.
        catch (UserNotAuthenticatedException)
        {
            CountSend(MetricConstants.MetricsResultUnauthenticatedLabelValue);
            throw;
        }
        // Not swallowed: an allocation failure says nothing about this turn, and a
        // retry allocates again and fails the same way. Letting it out sheds the
        // circuit, not the process. Counted so every attempt still lands in exactly
        // one Result bucket; deliberately absent from the Failure counter, the one
        // place those two metrics do not reconcile.
        catch (OutOfMemoryException)
        {
            CountSend(MetricConstants.MetricsResultFailedLabelValue);
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

    // Every press of send lands on exactly one Result, so the outcomes sum to the
    // attempts. That holds only if each call site sits where the turn cannot leave
    // — for Success, after the last await rather than before it.
    //
    // Prometheus fixes a metric's label names on first use and throws if a later
    // call supplies a different number, so every outcome must report the same
    // names in the same order. Building them here is what guarantees that.
    private void CountSend(string result, string? modelId = null) =>
        _metrics.Count(
            $"{MetricPrefix}_Send",
            "Chat send attempts, by outcome",
            (MetricConstants.MetricsModelLabelName, modelId ?? MetricConstants.MetricsModelUnknownLabelValue),
            (MetricConstants.MetricsResultLabelName, result));

    // Swallows by design: the turn is lost, the chat is not. The exception is
    // written here and only here — never shown, never persisted.
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

    // The partial response is discarded; why the turn ended is kept, so a reload
    // explains the gap instead of showing an unanswered message.
    //
    // The in-memory entry is added unconditionally, the write is not: a turn can
    // end before there is a chat row to write to, and that is exactly when the
    // user most needs to see something on screen.
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
            // Not the turn's own source — it may already be cancelled, and would
            // abort the very write recording that cancellation.
            await _repo.AppendEventAsync(userObjectId, _currentChat.Id, chatEvent, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Must not replace the outcome it was recording. The entry is already
            // in memory, so only a reload would lose it.
            _logger.LogWarning(ex, "Could not persist {Kind} event for chat {ChatId}", kind, _currentChat.Id);
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
        _channel.StreamStart(streamId);

        _currentChat ??= await _repo.CreateChatAsync(userObjectId, BuildTitle(text), ct);

        var entity = ChatMessageMapper.ToEntity(userMessage, systemPromptForThisTurn);
        await _repo.AppendMessagesAsync(userObjectId, _currentChat.Id, [entity], ct);

        return streamId;
    }

    private async Task<(ChatResponse Response, long DurationMs, long? FirstTokenMs)> StreamAssistantResponseAsync(Guid streamId, string systemPromptForThisTurn, CancellationToken ct)
    {
        var duration = _metrics.Histogram($"{MetricPrefix}_Duration", "Elapsed time for a chat message");

        // Stopwatch, not UtcNow: FlushCadence needs offsets that never go
        // backwards, and a wall clock does when NTP steps it — which would freeze
        // the visible stream until the clock caught up.
        var elapsed = Stopwatch.StartNew();
        long? firstTokenMs = null;
        var updates = new List<ChatResponseUpdate>();
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

    // The browser stopped listening. Same effect on the turn as Cancel, different
    // reason — and the reason decides whether the reloaded transcript says the
    // user stopped it or the connection did. Everything that can notice a dead
    // circuit comes through here, so one event cannot be filed under two names.
    public void CancelForDisconnect() => _turnCancellation?.CancelForDisconnect();

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
