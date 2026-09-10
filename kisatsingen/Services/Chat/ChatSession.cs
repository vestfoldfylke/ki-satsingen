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

    private static readonly string MetricPrefix = $"{MetricConstants.MetricsAppPrefix}_Chat";

    private readonly IAuthenticationService _authenticationService;
    private readonly IChatClient _client;
    private readonly IChatRepository _repo;
    private readonly IMetricsService _metrics;
    private readonly ChatClientChannel _channel;
    private readonly ILogger<ChatSession> _logger;

    private readonly List<TranscriptEntry> _entries = [];
    private Guid? _streamingId;
    private Data.Entities.Chat? _currentChat;
    private CancellationTokenSource? _cts;
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
        _logger = logger;

        // TryCancel as the disconnect handler: a lost circuit means nobody is
        // reading the stream, so the turn producing it should stop.
        _channel = new ChatClientChannel(js, logger, TryCancel);
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
        _cts = new CancellationTokenSource();

        // Hoisted so the cancellation handler can record the stop against the
        // right owner. Null means the stop beat authentication, in which case no
        // turn was ever started and there is nothing to record.
        string? userObjectId = null;

        try
        {
            userObjectId = await _authenticationService.RequireUserObjectIdentifierAsync();
            var systemPromptForThisTurn = _effectiveSystemPrompt;
            var streamId = await PersistUserTurnAsync(userObjectId, text.Trim(), systemPromptForThisTurn, _cts.Token);
            var (response, durationMs, firstTokenMs) = await StreamAssistantResponseAsync(streamId, systemPromptForThisTurn, _cts.Token);
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
                _channel.StreamEnd(id);
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

        // Per turn, and it has to stay that way — together with the stopwatch
        // above. Hoisting either to a field breaks the other: a reused cadence
        // remembers the previous turn's last flush while a fresh stopwatch
        // restarts offsets at zero, so the window never elapses and the whole
        // next turn looks frozen until 400 characters pile up.
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
