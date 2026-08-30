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
    private readonly StringBuilder _streamingText = new();
    private Guid? _streamingId;
    private Data.Entities.Chat? _currentChat;
    private CancellationTokenSource? _cts;

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
    public string StreamingText => _streamingText.ToString();

    public IReadOnlyList<ChatMessageView> Committed
    {
        get
        {
            var list = new List<ChatMessageView>(_messages.Count);
            foreach (var message in _messages)
            {
                if (message.Role == ChatRole.System)
                {
                    continue;
                }

                list.Add(new ChatMessageView(
                    GetOrCreateId(message),
                    message.Role,
                    message.Text ?? string.Empty,
                    message.Contents));
            }

            return list;
        }
    }

    public async Task LoadAsync(Guid? chatId, CancellationToken ct = default)
    {
        _messages.Clear();
        _messages.Add(new ChatMessage(ChatRole.System, SystemPrompt));
        _messageIds.Clear();
        _streamingText.Clear();
        _streamingId = null;
        _currentChat = null;

        if (chatId is not null)
        {
            var chat = await _repo.GetChatAsync(chatId.Value);
            if (chat is not null)
            {
                _currentChat = chat;
                foreach (var stored in chat.Messages)
                {
                    _messages.Add(ChatMessageMapper.FromEntity(stored));
                }
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
            _streamingText.Clear();
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
        _streamingText.Clear();
        _streamingId = Guid.NewGuid();
        Notify();

        FireAndForget("chatClient.streamStart", _streamingId);

        _currentChat ??= await _repo.CreateChatAsync(ownerId: null, BuildTitle(text), ct);
        await _repo.AppendMessageAsync(_currentChat.Id, ChatMessageMapper.ToEntity(userMessage), ct);
    }

    private async Task<(ChatResponse Response, long DurationMs, long? FirstTokenMs)> StreamAssistantResponseAsync(CancellationToken ct)
    {
        var duration = _metrics.Histogram($"{MetricPrefix}_Duration", "Elapsed time for a chat message");
        var startedAt = DateTimeOffset.UtcNow;
        long? firstTokenMs = null;
        var updates = new List<ChatResponseUpdate>();

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
            _streamingText.Append(update.Text);
            FireAndForget("chatClient.streamAppend", _streamingId, update.Text);
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

        foreach (var newMessage in response.Messages)
        {
            _messages.Add(newMessage);
            await _repo.AppendMessageAsync(_currentChat!.Id, ChatMessageMapper.ToEntity(newMessage, response, durationMs, firstTokenMs), ct);
        }
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

    private void FireAndForget(string method, params object?[] args)
    {
        _ = ObserveAsync();
        return;

        async Task ObserveAsync()
        {
            try
            {
                await _js.InvokeVoidAsync(method, args);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "JS interop failed for {Method}", method);
            }
        }
    }

    public void Cancel() => _cts?.Cancel();

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
