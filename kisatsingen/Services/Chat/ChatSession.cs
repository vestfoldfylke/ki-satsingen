using System.Text;
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

    private static readonly string MetricPrefix = $"{MetricConstants.MetricsAppPrefix}_Chat";

    private readonly IAuthenticationService _authenticationService;
    private readonly IChatClient _client;
    private readonly IChatRepository _repo;
    private readonly IMetricsService _metrics;
    private readonly IJSRuntime _js;
    private readonly ILogger<ChatSession> _logger;

    private readonly List<TranscriptEntry> _entries = [];
    private Guid? _streamingId;
    private Data.Entities.Chat? _currentChat;
    private CancellationTokenSource? _cts;
    private const int CircuitLive = 0;
    private const int CircuitLost = 1;
    private int _circuitState;
    private string _effectiveSystemPrompt = DefaultSystemPrompt;

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
        _js = js;
        _logger = logger;
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
                RestoreEntries(chat);
            }
        }

        Notify();
    }

    // Messages and events are stored apart but ordered together. Both arrive
    // sorted by Seq, and Seq is unique across the two, so a straight merge
    // rebuilds the original sequence with no tie to break.
    private void RestoreEntries(Data.Entities.Chat chat)
    {
        var messages = chat.Messages;
        var events = chat.Events;
        var messageIndex = 0;
        var eventIndex = 0;

        // Each user turn records the system prompt in force when it was sent, so
        // assistant metadata can report the prompt that actually produced it.
        var currentSnapshot = _effectiveSystemPrompt;

        while (messageIndex < messages.Count || eventIndex < events.Count)
        {
            var takeMessage = eventIndex >= events.Count
                || (messageIndex < messages.Count && messages[messageIndex].Seq < events[eventIndex].Seq);

            if (takeMessage)
            {
                var stored = messages[messageIndex++];
                var role = new ChatRole(stored.Role);

                AssistantMetadata? metadata = null;
                if (role == ChatRole.User)
                {
                    currentSnapshot = stored.SystemPromptSnapshot ?? currentSnapshot;
                }
                else if (role == ChatRole.Assistant)
                {
                    metadata = new AssistantMetadata(
                        stored.ModelId,
                        stored.ResponseId,
                        stored.FinishReason,
                        MessageUsage.FromEntity(stored),
                        stored.DurationMs,
                        stored.TimeToFirstTokenMs,
                        stored.CreatedAt,
                        currentSnapshot);
                }

                _entries.Add(new MessageEntry(Guid.NewGuid(), ChatMessageMapper.FromEntity(stored), metadata));
                continue;
            }

            var storedEvent = events[eventIndex++];
            _entries.Add(new EventEntry(Guid.NewGuid(), storedEvent.Kind, storedEvent.Detail, storedEvent.CreatedAt));
        }
    }

    public async Task SendAsync(string text)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        IsBusy = true;
        _cts = new CancellationTokenSource();

        // Hoisted so the cancellation handler can record the stop against the
        // right owner. Null means the stop beat authentication, in which case no
        // turn was ever started and there is nothing to record.
        string? userObjectId = null;

        try
        {
            userObjectId = await _authenticationService.RequireUserObjectIdentifierAsync();
            var systemPromptForThisTurn = _effectiveSystemPrompt;
            await PersistUserTurnAsync(userObjectId, text.Trim(), systemPromptForThisTurn, _cts.Token);
            var (response, durationMs, firstTokenMs) = await StreamAssistantResponseAsync(systemPromptForThisTurn, _cts.Token);
            await PersistResponseAsync(userObjectId, response, durationMs, firstTokenMs, systemPromptForThisTurn, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            CountSend(MetricConstants.MetricsResultCancelledLabelValue);
            await RecordStoppedAsync(userObjectId);
        }
        catch (UserNotAuthenticatedException)
        {
            CountSend(MetricConstants.MetricsResultUnauthenticatedLabelValue);
            throw;
        }
        finally
        {
            if (_streamingId is Guid id)
            {
                FireAndForget("chatClient.streamEnd", id);
            }
            _streamingId = null;
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
            Notify();
        }
    }

    // Counts attempts rather than deliveries, which is why an unauthenticated
    // caller belongs here too: every press of send lands on exactly one Result,
    // so the outcomes sum to the attempts.
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

    // The partial response is deliberately discarded — the user asked for it to
    // stop. What is kept is that a stop happened here, so reloading the chat
    // shows why a turn produced nothing instead of an unexplained gap.
    private async Task RecordStoppedAsync(string? userObjectId)
    {
        if (userObjectId is null || _currentChat is null)
        {
            return;
        }

        var stopped = new Data.Entities.ChatEvent { Kind = ChatEventKind.Stopped };
        _entries.Add(new EventEntry(Guid.NewGuid(), stopped.Kind, stopped.Detail, DateTimeOffset.UtcNow));

        try
        {
            // Deliberately not the turn's cancellation source: it is already
            // cancelled here, and using it would abort the very write that
            // records the cancellation.
            await _repo.AppendEventAsync(userObjectId, _currentChat.Id, stopped, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Failing to record the stop must not replace the original
            // cancellation or take down the circuit. The entry is already in
            // memory, so this turn still renders correctly; only a reload of the
            // chat would lose it.
            _logger.LogWarning(ex, "Could not persist stop event for chat {ChatId}", _currentChat.Id);
        }
    }

    private async Task PersistUserTurnAsync(string userObjectId, string text, string systemPromptForThisTurn, CancellationToken ct)
    {
        var userMessage = new ChatMessage(ChatRole.User, text);
        _entries.Add(new MessageEntry(Guid.NewGuid(), userMessage, null));
        _streamingId = Guid.NewGuid();
        Notify();

        FireAndForget("chatClient.streamStart", _streamingId);

        _currentChat ??= await _repo.CreateChatAsync(userObjectId, BuildTitle(text), ct);

        var entity = ChatMessageMapper.ToEntity(userMessage, systemPromptForThisTurn);
        await _repo.AppendMessagesAsync(userObjectId, _currentChat.Id, [entity], ct);
    }

    // Flush thresholds tuned for streams roughly in the 20-200 tok/s range.
    // Flush more often -> more SignalR msgs/s and higher server CPU under fan-out;
    // less often -> visible pauses in the streaming UI. Retune if either shows up
    // under load. The rule below is cadence-adaptive (Nagle-style): a solitary
    // token in a slow stream is flushed immediately; a burst is coalesced.
    private const long FlushIntervalMs = 50;
    private const int FlushCharThreshold = 400;

    private async Task<(ChatResponse Response, long DurationMs, long? FirstTokenMs)> StreamAssistantResponseAsync(string systemPromptForThisTurn, CancellationToken ct)
    {
        var duration = _metrics.Histogram($"{MetricPrefix}_Duration", "Elapsed time for a chat message");
        var startedAt = DateTimeOffset.UtcNow;
        long? firstTokenMs = null;
        var updates = new List<ChatResponseUpdate>();
        var buffer = new StringBuilder();
        // Seed so the first token counts as "long-idle since last flush/token"
        // and gets flushed eagerly, giving a truthful TTFT on the client.
        var lastFlushMs = -FlushIntervalMs;
        var lastTokenMs = -FlushIntervalMs;

        var request = TranscriptRequest.Build(_entries, systemPromptForThisTurn);

        await foreach (var update in _client.GetStreamingResponseAsync(request, Options, ct))
        {
            updates.Add(update);
            var offsetMs = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;

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
            buffer.Append(update.Text);

            var slowStream = offsetMs - lastTokenMs >= FlushIntervalMs;
            var windowElapsed = offsetMs - lastFlushMs >= FlushIntervalMs;
            var bufferFull = buffer.Length >= FlushCharThreshold;
            lastTokenMs = offsetMs;

            if (slowStream || windowElapsed || bufferFull)
            {
                FireAndForget("chatClient.streamAppend", _streamingId, buffer.ToString());
                buffer.Clear();
                lastFlushMs = offsetMs;
            }
        }

        if (buffer.Length > 0)
        {
            FireAndForget("chatClient.streamAppend", _streamingId, buffer.ToString());
        }

        var response = updates.ToChatResponse();
        var durationMs = (long)duration.ObserveDuration().TotalMilliseconds;
        return (response, durationMs, firstTokenMs);
    }

    private async Task PersistResponseAsync(string userObjectId, ChatResponse response, long durationMs, long? firstTokenMs, string systemPromptForThisTurn, CancellationToken ct)
    {
        CountSend(MetricConstants.MetricsResultSuccessLabelValue, response.ModelId);

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
    }

    private static string BuildTitle(string userText)
    {
        var trimmed = userText.Trim();
        return trimmed.Length <= 60 ? trimmed : trimmed[..60].TrimEnd() + "…";
    }

    private void Notify() => StateChanged?.Invoke();

    private bool IsCircuitLost =>
        Volatile.Read(ref _circuitState) == CircuitLost;

    private bool TryMarkCircuitLost() =>
        Interlocked.Exchange(ref _circuitState, CircuitLost) == CircuitLive;

    private void FireAndForget(string method, params object?[] args)
    {
        if (IsCircuitLost)
        {
            return;
        }

        _ = ObserveAsync();
        return;

        async Task ObserveAsync()
        {
            try
            {
                await _js.InvokeVoidAsync(method, args);
            }
            catch (JSDisconnectedException)
            {
                if (TryMarkCircuitLost())
                {
                    _logger.LogInformation("Client circuit disconnected; cancelling active stream.");
                    TryCancel();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "JS interop failed for {Method}", method);
            }
        }
    }

    public void Cancel() => TryCancel();

    // Guards against _cts being disposed by SendAsync's finally block on a
    // parallel thread. A disposed CTS is already cancelled from a caller's
    // perspective, so swallowing is correct.
    private void TryCancel()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync();
        _cts.Dispose();
        _cts = null;
    }
}
