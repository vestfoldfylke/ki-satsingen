using System.Text;
using kisatsingen.AIFunctions;
using kisatsingen.Constants;
using kisatsingen.Data.Repositories;
using Microsoft.Extensions.AI;
using Microsoft.JSInterop;
using Vestfold.Extensions.Metrics.Services;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Services.Chat;

public sealed class ChatSession : IAsyncDisposable
{
    private const string SystemPrompt = "You are a concise, helpful assistant. Use tools when they help.";

    private static readonly ChatOptions Options = new()
    {
        Tools = [ChatTools.GetCurrentTimeUtcTool]
    };

    private static readonly string MetricPrefix = $"{MetricConstants.MetricsAppPrefix}_Chat";

    private readonly IChatClient _client;
    private readonly IChatRepository _repo;
    private readonly IMetricsService _metrics;
    private readonly IJSRuntime _js;
    private readonly ILogger<ChatSession> _logger;

    private readonly List<ChatMessage> _messages = [];
    private readonly Dictionary<ChatMessage, Guid> _messageIds = new();
    private readonly Dictionary<ChatMessage, AssistantMetadata> _messageMetadata = [];
    private Guid? _streamingId;
    private Data.Entities.Chat? _currentChat;
    private CancellationTokenSource? _cts;
    private const int CircuitLive = 0;
    private const int CircuitLost = 1;
    private int _circuitState;
    private string? _effectiveSystemPrompt;

    public event Action? StateChanged;

    public ChatSession(
        IChatClient client,
        IChatRepository repo,
        IMetricsService metrics,
        IJSRuntime js,
        ILogger<ChatSession> logger)
    {
        _client = client;
        _repo = repo;
        _metrics = metrics;
        _js = js;
        _logger = logger;
    }

    public Guid? ChatId => _currentChat?.Id;
    public bool IsBusy { get; private set; }
    public bool HasVisibleMessages => _messages.Any(m => m.Role != ChatRole.System) || _streamingId is not null;

    public Guid? StreamingId => _streamingId;

    public IReadOnlyList<ChatItemView> Committed
    {
        get
        {
            var list = new List<ChatItemView>();
            List<ChatMessage>? currentTurn = null;

            foreach (var message in _messages)
            {
                if (message.Role == ChatRole.System)
                {
                    continue;
                }

                if (message.Role == ChatRole.User)
                {
                    if (currentTurn is not null)
                    {
                        list.Add(BuildTurnView(currentTurn));
                        currentTurn = null;
                    }
                    list.Add(new UserBubbleView(GetOrCreateId(message), message.Text ?? string.Empty));
                    continue;
                }

                currentTurn ??= [];
                currentTurn.Add(message);
            }

            if (currentTurn is not null)
            {
                list.Add(BuildTurnView(currentTurn));
            }

            return list;
        }
    }

    private AssistantTurnView BuildTurnView(List<ChatMessage> turnMessages)
    {
        var parts = new List<TurnPart>(turnMessages.Count);
        foreach (var message in turnMessages)
        {
            parts.Add(new TurnPart(message.Text ?? string.Empty, message.Contents));
        }

        AssistantMetadata? firstMetadata = null;
        AssistantMetadata? lastMetadata = null;
        foreach (var message in turnMessages)
        {
            if (message.Role != ChatRole.Assistant)
            {
                continue;
            }
            if (!_messageMetadata.TryGetValue(message, out var found))
            {
                continue;
            }
            firstMetadata ??= found;
            lastMetadata = found;
        }

        return new AssistantTurnView(
            GetOrCreateId(turnMessages[0]),
            parts,
            lastMetadata,
            firstMetadata?.CreatedAt);
    }

    public MessageUsage? ConversationUsage
    {
        get
        {
            long input = 0;
            long output = 0;
            long total = 0;
            var hasAny = false;
            foreach (var metadata in _messageMetadata.Values)
            {
                if (metadata.Usage is not { } usage)
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
        _messages.Clear();
        _messageIds.Clear();
        _messageMetadata.Clear();
        _streamingId = null;
        _currentChat = null;
        _effectiveSystemPrompt = null;

        if (chatId is not null)
        {
            var chat = await _repo.GetChatAsync(chatId.Value, ct);
            if (chat is not null)
            {
                _currentChat = chat;
                foreach (var stored in chat.Messages)
                {
                    var message = ChatMessageMapper.FromEntity(stored);
                    _messages.Add(message);

                    if (message.Role == ChatRole.System)
                    {
                        _effectiveSystemPrompt = stored.Content;
                        continue;
                    }

                    if (message.Role == ChatRole.Assistant)
                    {
                        _messageMetadata[message] = new AssistantMetadata(
                            stored.ModelId,
                            stored.ResponseId,
                            stored.FinishReason,
                            MessageUsage.FromEntity(stored),
                            stored.DurationMs,
                            stored.TimeToFirstTokenMs,
                            stored.CreatedAt,
                            _effectiveSystemPrompt);
                    }
                }
            }
        }

        if (!_messages.Any(m => m.Role == ChatRole.System))
        {
            _messages.Insert(0, new ChatMessage(ChatRole.System, SystemPrompt));
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

        try
        {
            await PersistUserTurnAsync(text.Trim(), _cts.Token);
            var (response, durationMs, firstTokenMs) = await StreamAssistantResponseAsync(_cts.Token);
            await PersistResponseAsync(response, durationMs, firstTokenMs, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            _metrics.Count($"{MetricPrefix}_Send", "Number of chats sent", (MetricConstants.MetricsResultLabelName, MetricConstants.MetricsResultFailedLabelValue));
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

    private async Task PersistUserTurnAsync(string text, CancellationToken ct)
    {
        var userMessage = new ChatMessage(ChatRole.User, text);
        _messages.Add(userMessage);
        _streamingId = Guid.NewGuid();
        Notify();

        FireAndForget("chatClient.streamStart", _streamingId);

        _currentChat ??= await _repo.CreateChatAsync(ownerId: null, BuildTitle(text), ct);

        var toPersist = new List<Data.Entities.ChatMessage>(2);
        if (_effectiveSystemPrompt != SystemPrompt)
        {
            toPersist.Add(ChatMessageMapper.ToEntity(new ChatMessage(ChatRole.System, SystemPrompt)));
            _effectiveSystemPrompt = SystemPrompt;
        }
        toPersist.Add(ChatMessageMapper.ToEntity(userMessage));

        await _repo.AppendMessagesAsync(_currentChat.Id, toPersist, ct);
    }

    // Flush thresholds tuned for streams roughly in the 20-200 tok/s range.
    // Flush more often -> more SignalR msgs/s and higher server CPU under fan-out;
    // less often -> visible pauses in the streaming UI. Retune if either shows up
    // under load. The rule below is cadence-adaptive (Nagle-style): a solitary
    // token in a slow stream is flushed immediately; a burst is coalesced.
    private const long FlushIntervalMs = 50;
    private const int FlushCharThreshold = 400;

    private async Task<(ChatResponse Response, long DurationMs, long? FirstTokenMs)> StreamAssistantResponseAsync(CancellationToken ct)
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

        await foreach (var update in _client.GetStreamingResponseAsync(_messages, Options, ct))
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

    private async Task PersistResponseAsync(ChatResponse response, long durationMs, long? firstTokenMs, CancellationToken ct)
    {
        if (response.ModelId is not null)
        {
            _metrics.Count($"{MetricPrefix}_Send", "Number of chats sent", ("Model", response.ModelId), (MetricConstants.MetricsResultLabelName, MetricConstants.MetricsResultSuccessLabelValue));
        }
        else
        {
            _metrics.Count($"{MetricPrefix}_Send", "Number of chats sent");
        }

        var lastAssistant = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant);
        var now = DateTimeOffset.UtcNow;
        var toPersist = new List<Data.Entities.ChatMessage>(response.Messages.Count);
        foreach (var newMessage in response.Messages)
        {
            _messages.Add(newMessage);
            var includeUsage = ReferenceEquals(newMessage, lastAssistant);

            if (newMessage.Role == ChatRole.Assistant)
            {
                var usage = includeUsage && response.Usage is { } u
                    ? new MessageUsage(u.InputTokenCount, u.OutputTokenCount, u.TotalTokenCount)
                    : null;
                _messageMetadata[newMessage] = new AssistantMetadata(
                    response.ModelId,
                    response.ResponseId,
                    response.FinishReason?.Value,
                    usage,
                    durationMs,
                    firstTokenMs,
                    now,
                    _effectiveSystemPrompt);
            }

            toPersist.Add(ChatMessageMapper.ToEntity(newMessage, response, durationMs, firstTokenMs, includeUsage));
        }

        await _repo.AppendMessagesAsync(_currentChat!.Id, toPersist, ct);
    }

    private Guid GetOrCreateId(ChatMessage message)
    {
        if (_messageIds.TryGetValue(message, out var id))
        {
            return id;
        }

        id = Guid.NewGuid();
        _messageIds[message] = id;
        return id;
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
        if (IsCircuitLost) return;
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
