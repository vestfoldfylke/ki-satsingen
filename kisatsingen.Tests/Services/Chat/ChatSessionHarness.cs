using kisatsingen.AIFunctions;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services;
using kisatsingen.Services.Chat;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Vestfold.Extensions.Metrics.Services;
using Xunit;
using AiMessage = Microsoft.Extensions.AI.ChatMessage;
// Aliased: this namespace's .Chat shadows the entity, and Prometheus.ITimer
// collides with System.Threading.ITimer.
using ChatEntity = kisatsingen.Data.Entities.Chat;
using MetricTimer = Prometheus.ITimer;

namespace kisatsingen.Tests.Services.Chat;

// Doubles only at the boundaries ChatSession sequences — model, database,
// browser. Everything else under test is real. Scripted through properties rather
// than a mocking library, so each test reads as the scenario it describes.
internal sealed class ChatSessionHarness : IAsyncDisposable
{
    public const string OwnerUnderTest = "owner-under-test";

    public FakeAuthenticationService Authentication { get; } = new();
    public FakeChatRepository Repository { get; } = new();
    public FakeChatClient Client { get; } = new();
    public FakeChatModelCatalog Catalog { get; }
    public RecordingMetricsService Metrics { get; } = new();
    public ChatSession Session { get; }

    public ChatManager Manager { get; }

    public ChatSessionHarness()
    {
        Catalog = new FakeChatModelCatalog(Client);
        Manager = new ChatManager(Authentication, Repository, NullLogger<ChatManager>.Instance);
        Session = new ChatSession(
            Authentication,
            Catalog,
            Repository,
            Manager,
            Metrics,
            new SilentJsRuntime(),
            NullLogger<ChatSession>.Instance);
    }

    // Through the public projection, so an outcome the UI can't render fails the test.
    public Turn VisibleTurn => Assert.Single(Session.Transcript).Turn;

    public string? VisibleNotice => TurnNotice.Describe(Assert.Single(Session.Transcript));

    // Read back through the real mapper, so a field the mapper drops fails the test.
    public Turn StoredTurn =>
        ChatTurnMapper.FromEntity(Repository.SingleStoredTurn, DateTimeOffset.UtcNow, key => key.Value, NullLogger.Instance);

    public ValueTask DisposeAsync() => Session.DisposeAsync();
}

internal static class TurnText
{
    public static string Of(Turn turn) => string.Concat(turn.Answer.OfType<TextSegment>().Select(text => text.Text));
}

internal sealed class FakeAuthenticationService : IAuthenticationService
{
    public Exception? Failure { get; set; }

    public Task<string?> GetUserObjectIdentifierAsync() =>
        Task.FromResult<string?>(ChatSessionHarness.OwnerUnderTest);

    public Task<string> RequireUserObjectIdentifierAsync() =>
        Failure is not null
            ? Task.FromException<string>(Failure)
            : Task.FromResult(ChatSessionHarness.OwnerUnderTest);
}

internal sealed class FakeChatRepository : IChatRepository
{
    public Exception? CreateChatFailure { get; set; }
    public Exception? InsertTurnFailure { get; set; }

    // Only the first update: "the answer couldn't be saved" fails that one, and
    // later updates are unscripted so a second send doesn't inherit the failure.
    public Exception? FirstUpdateFailure { get; set; }

    // Lets a test act while the turn is genuinely mid-save.
    public Action? BeforeFirstUpdate { get; set; }

    // Lets a test act while the chat row is being created.
    public Func<Task>? BeforeCreateChat { get; set; }

    // Lets a test hold one load open while another completes.
    public Func<Guid, Task>? BeforeGetChat { get; set; }

    // Every write of every turn, oldest first.
    public List<TurnWrite> Writes { get; } = [];

    private int _updateCalls;
    private readonly Dictionary<Guid, ChatEntity> _storedChats = [];

    // The latest write of each turn, in the order the turns were first written.
    public IReadOnlyList<ChatTurn> StoredTurns =>
        Writes.GroupBy(write => write.Turn.Id).Select(writes => writes.Last().Turn).ToList();

    public ChatTurn SingleStoredTurn => Assert.Single(StoredTurns);

    public ChatEntity Store(ChatEntity chat)
    {
        _storedChats[chat.Id] = chat;
        return chat;
    }

    public async Task<ChatEntity> CreateChatAsync(string ownerId, string title, Guid? assistantId, CancellationToken ct = default)
    {
        if (BeforeCreateChat is not null)
        {
            await BeforeCreateChat();
        }

        if (CreateChatFailure is not null)
        {
            throw CreateChatFailure;
        }

        return new ChatEntity
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            AssistantId = assistantId,
            Title = title,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<ChatEntity?> GetChatAsync(string ownerId, Guid chatId, CancellationToken ct = default)
    {
        if (BeforeGetChat is not null)
        {
            await BeforeGetChat(chatId);
        }

        return _storedChats.GetValueOrDefault(chatId);
    }

    public Task<IReadOnlyList<ChatSummary>> ListChatsAsync(string ownerId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ChatSummary>>([]);

    public Task InsertTurnAsync(string ownerId, Guid chatId, ChatTurn turn, CancellationToken ct = default)
    {
        if (InsertTurnFailure is not null)
        {
            return Task.FromException(InsertTurnFailure);
        }

        Writes.Add(new TurnWrite(chatId, turn));
        return Task.CompletedTask;
    }

    public Task UpdateTurnAsync(string ownerId, Guid chatId, ChatTurn turn, CancellationToken ct = default)
    {
        var isFirst = ++_updateCalls == 1;
        if (isFirst)
        {
            BeforeFirstUpdate?.Invoke();
        }

        var failure = isFirst ? FirstUpdateFailure : null;
        if (failure is not null)
        {
            return Task.FromException(failure);
        }

        Writes.Add(new TurnWrite(chatId, turn));
        return Task.CompletedTask;
    }

    public Task RenameChatAsync(string ownerId, Guid chatId, string title, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<bool> DeleteChatAsync(string ownerId, Guid chatId, CancellationToken ct = default) =>
        Task.FromResult(true);
}

internal sealed record TurnWrite(Guid ChatId, ChatTurn Turn);

internal sealed class FakeChatClient : IChatClient
{
    // Answers by default, so a test that isn't about the model needn't mention it.
    public Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> OnStream { get; set; } =
        _ => ModelStream.Answering("Hei");

    // Kept because the system prompt travels on the options, not in the messages.
    public ChatOptions? LastOptions { get; private set; }

    public IReadOnlyList<AiMessage> LastMessages { get; private set; } = [];

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<AiMessage> messages,
        ChatOptions? options = null,
        CancellationToken ct = default)
    {
        // Materialised: the contract allows a caller to pass a lazy sequence.
        LastMessages = messages.ToList();
        LastOptions = options;
        return OnStream(ct);
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<AiMessage> messages,
        ChatOptions? options = null,
        CancellationToken ct = default) =>
        throw new NotSupportedException("ChatSession only ever streams.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

// Both models share one client: these tests are about which model is picked and
// recorded, not about providers behaving differently.
internal sealed class FakeChatModelCatalog : IChatModelCatalog
{
    public static readonly ChatModelKey DefaultKey = new("fast-under-test");
    public static readonly ChatModelKey AlternativeKey = new("large-under-test");
    public static readonly ChatModelKey UnknownKey = new("not-registered");

    private readonly ChatModel[] _models;
    private readonly Dictionary<ChatModelKey, ChatModelRuntime> _runtimes;

    public FakeChatModelCatalog(IChatClient client)
    {
        _models = [Model(DefaultKey, "Fast"), Model(AlternativeKey, "Large")];
        _runtimes = _models.ToDictionary(
            model => model.Key,
            model => new ChatModelRuntime(model, client, new ChatOptions { Tools = [.. ChatTools.All] }));

        Default = _models[0];
    }

    public ChatModel Default { get; }

    // With one shared client, the only way to see which model a turn ran on.
    public List<ChatModelKey> ResolvedKeys { get; } = [];

    public IReadOnlyList<ChatModel> Models => _models;

    public bool TryGet(ChatModelKey key, [MaybeNullWhen(false)] out ChatModel model)
    {
        model = _models.FirstOrDefault(candidate => candidate.Key == key);
        return model is not null;
    }

    public ChatModelRuntime Resolve(ChatModelKey key)
    {
        ResolvedKeys.Add(key);
        return _runtimes[key];
    }

    private static ChatModel Model(ChatModelKey key, string displayName) => new()
    {
        Key = key,
        DisplayName = displayName,
        ShortDescription = $"{displayName}, briefly",
        LongDescription = $"{displayName}, at length",
        Provider = "test",
        ModelId = $"{key}-wire-id",
        ContextWindowTokens = 128_000,
        IconName = "test-icon"
    };
}

internal static class ModelStream
{
    public static async IAsyncEnumerable<ChatResponseUpdate> Answering(string text)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, text);
        await Task.CompletedTask;
    }

    // Usage as a trailing chunk, the way providers send it.
    public static async IAsyncEnumerable<ChatResponseUpdate> AnsweringWithUsage(string text, long inputTokens, long outputTokens)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, text);
        yield return Usage(inputTokens, outputTokens);
        await Task.CompletedTask;
    }

    // The shape FunctionInvokingChatClient streams: the call, then its result as a
    // tool message, then the next round's answer.
    public static async IAsyncEnumerable<ChatResponseUpdate> UsingATool(string before, string after)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, before);
        yield return new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("call-1", "get_current_time_utc")]);
        yield return new ChatResponseUpdate(ChatRole.Tool, [new FunctionResultContent("call-1", "2026-09-28T12:00:00Z")]);
        yield return new ChatResponseUpdate(ChatRole.Assistant, after);
        await Task.CompletedTask;
    }

    // Text first: by the time a provider gives up, tokens have reached the browser.
    public static async IAsyncEnumerable<ChatResponseUpdate> FailingAfter(string text, Exception failure)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, text);
        await Task.Yield();
        throw failure;
    }

    // The realistic stop: the user has read enough and has what they need.
    public static async IAsyncEnumerable<ChatResponseUpdate> AnsweringThenStalling(
        string text,
        TaskCompletionSource reached,
        [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, text);
        reached.TrySetResult();
        await Task.Delay(Timeout.Infinite, ct);
    }

    // A completed round trip, then an interrupted one.
    public static async IAsyncEnumerable<ChatResponseUpdate> AnsweringWithUsageThenStalling(
        string text,
        long inputTokens,
        long outputTokens,
        TaskCompletionSource reached,
        [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, text);
        yield return Usage(inputTokens, outputTokens);
        reached.TrySetResult();
        await Task.Delay(Timeout.Infinite, ct);
    }

    // Stopped while the tool runs: a call that nothing answered.
    public static async IAsyncEnumerable<ChatResponseUpdate> CallingAToolThenStalling(
        TaskCompletionSource reached,
        [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant,
            [new FunctionCallContent("unanswered-call", "get_weather", new Dictionary<string, object?>())]);
        reached.TrySetResult();
        await Task.Delay(Timeout.Infinite, ct);
    }

    // So a test cancels a turn genuinely in flight, not one already finished.
    public static async IAsyncEnumerable<ChatResponseUpdate> Stalling(
        TaskCompletionSource reached,
        [EnumeratorCancellation] CancellationToken ct)
    {
        reached.TrySetResult();
        await Task.Delay(Timeout.Infinite, ct);
        yield break;
    }

    private static ChatResponseUpdate Usage(long inputTokens, long outputTokens) =>
        new(ChatRole.Assistant,
        [
            new UsageContent(new UsageDetails
            {
                InputTokenCount = inputTokens,
                OutputTokenCount = outputTokens,
                TotalTokenCount = inputTokens + outputTokens
            })
        ]);
}

// Records names and labels, so tests assert on what ops would see rather than
// merely that something was counted.
internal sealed class RecordingMetricsService : IMetricsService
{
    public const string SendCounter = "_Send";
    public const string FailureCounter = "_Failure";

    public List<MetricCall> Calls { get; } = [];

    // What Prometheus does on a label mismatch, for one metric only.
    public string? ThrowForNameEndingWith { get; set; }

    public IReadOnlyList<MetricCall> Named(string suffix) =>
        Calls.Where(call => call.Name.EndsWith(suffix, StringComparison.Ordinal)).ToList();

    public void Count(string name, string? description, int value = 1) => Record(name, []);

    public void Count(string name, string? description, params (string, string)[] labels) => Record(name, labels);

    public void Count(string name, string? description, int value, params (string, string)[] labels) => Record(name, labels);

    public void Gauge(string name, double value) => Record(name, []);

    public void Gauge(string name, string description, double value) => Record(name, []);

    public void Gauge(string name, double value, params (string, string)[] labels) => Record(name, labels);

    public void Gauge(string name, string description, double value, params (string, string)[] labels) => Record(name, labels);

    public MetricTimer Histogram(string name, string? description) => new ZeroTimer();

    public MetricTimer Histogram(string name, params (string, string)[] labels) => new ZeroTimer();

    public MetricTimer Histogram(string name, string? description, params (string, string)[] labels) => new ZeroTimer();

    private void Record(string name, (string, string)[] labels)
    {
        if (ThrowForNameEndingWith is { } suffix && name.EndsWith(suffix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Scripted metrics failure for {name}.");
        }

        Calls.Add(new MetricCall(name, labels.ToDictionary(label => label.Item1, label => label.Item2)));
    }

    // Nothing asserts on durations.
    private sealed class ZeroTimer : MetricTimer
    {
        public TimeSpan ObserveDuration() => TimeSpan.Zero;

        public void Dispose()
        {
        }
    }
}

internal sealed record MetricCall(string Name, IReadOnlyDictionary<string, string> Labels)
{
    public string Label(string name) => Labels.TryGetValue(name, out var value) ? value : "<absent>";
}

// Nothing to assert about the browser here: FlushCadence and ChatClientChannel
// have their own tests.
internal sealed class SilentJsRuntime : IJSRuntime
{
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => default;

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args) => default;
}
