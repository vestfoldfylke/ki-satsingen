using kisatsingen.AIFunctions;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services;
using kisatsingen.Services.Chat;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Vestfold.Extensions.Metrics.Services;
using Xunit;
using AiMessage = Microsoft.Extensions.AI.ChatMessage;
// This namespace ends in .Chat, which shadows the entity of the same name, and
// Prometheus.ITimer collides with System.Threading.ITimer. Both are aliased
// rather than imported so neither ambiguity can come back.
using ChatEntity = kisatsingen.Data.Entities.Chat;
using MetricTimer = Prometheus.ITimer;
using StoredMessage = kisatsingen.Data.Entities.ChatMessage;

namespace kisatsingen.Tests.Services.Chat;

// ChatSession is the one piece of the chat stack that cannot be made pure: it
// exists to sequence a model, a database, a browser and a clock. So it gets
// doubles at exactly those boundaries and nothing else — the transcript, the
// outcome rules and the cancellation wiring under test are all the real thing.
//
// Every double is scripted through a property rather than a mocking library, so
// each test reads as the scenario it describes: "the provider throws this",
// "the second save fails".
internal sealed class ChatSessionHarness : IAsyncDisposable
{
    // Opaque to the session, which only ever passes it through to the repository.
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

    // What the user would see in the transcript. Read through the public
    // projection rather than the session's private entry list, so a session that
    // records an outcome the UI cannot render fails here.
    public IReadOnlyList<ChatEventView> VisibleEvents =>
        Session.Committed.OfType<ChatEventView>().ToList();

    public ChatEventView SingleVisibleEvent => Assert.Single(VisibleEvents);

    public ValueTask DisposeAsync() => Session.DisposeAsync();
}

internal sealed class FakeAuthenticationService : IAuthenticationService
{
    public Exception? Failure { get; set; }

    // Authenticated but claimless. The catalogue ignores the principal today, and
    // a test that cares about claims should build its own rather than have every
    // other test carry them.
    public Task<ClaimsPrincipal> GetUserAsync() =>
        Task.FromResult(new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test")));

    public Task<string?> GetUserObjectIdentifierAsync() =>
        Task.FromResult<string?>(ChatSessionHarness.OwnerUnderTest);

    public Task<string> RequireUserObjectIdentifierAsync() =>
        Failure is not null
            ? Task.FromException<string>(Failure)
            : Task.FromResult(ChatSessionHarness.OwnerUnderTest);
}

internal sealed class FakeChatRepository : IChatRepository
{
    public List<ChatEvent> AppendedEvents { get; } = [];
    public List<StoredMessage> AppendedMessages { get; } = [];

    public Exception? CreateChatFailure { get; set; }
    public Exception? AppendEventFailure { get; set; }

    // Scripted by call index, because one turn appends twice — the user's message,
    // then the model's reply — and "the answer could not be saved" is precisely
    // the case where only the second fails. Appends past the second are
    // unscripted, so a test that sends twice does not silently inherit the first
    // turn's script.
    public Exception? FirstAppendMessagesFailure { get; set; }
    public Exception? SecondAppendMessagesFailure { get; set; }

    // Runs just before the second append, so a test can make something happen
    // while the turn is genuinely mid-save rather than before or after it.
    public Action? BeforeSecondAppendMessages { get; set; }

    private int _appendMessagesCalls;

    public Task<ChatEntity> CreateChatAsync(string ownerId, string title, Guid? assistantId, CancellationToken ct = default) =>
        CreateChatFailure is not null
            ? Task.FromException<ChatEntity>(CreateChatFailure)
            : Task.FromResult(new ChatEntity
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                AssistantId = assistantId,
                Title = title,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

    // What LoadAsync finds on disk. Null is the default because most of these
    // tests start a chat rather than reopen one.
    public ChatEntity? StoredChat { get; set; }

    public Task<ChatEntity?> GetChatAsync(string ownerId, Guid chatId, CancellationToken ct = default) =>
        Task.FromResult(StoredChat);

    public Task<IReadOnlyList<ChatSummary>> ListChatsAsync(string ownerId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ChatSummary>>([]);

    public Task AppendMessagesAsync(string ownerId, Guid chatId, IReadOnlyList<StoredMessage> messages, CancellationToken ct = default)
    {
        var call = ++_appendMessagesCalls;
        if (call == 2)
        {
            BeforeSecondAppendMessages?.Invoke();
        }

        var failure = call switch
        {
            1 => FirstAppendMessagesFailure,
            2 => SecondAppendMessagesFailure,
            _ => null
        };

        if (failure is not null)
        {
            return Task.FromException(failure);
        }

        AppendedMessages.AddRange(messages);
        return Task.CompletedTask;
    }

    public Task AppendEventAsync(string ownerId, Guid chatId, ChatEvent chatEvent, CancellationToken ct = default)
    {
        if (AppendEventFailure is not null)
        {
            return Task.FromException(AppendEventFailure);
        }

        AppendedEvents.Add(chatEvent);
        return Task.CompletedTask;
    }

    public Task RenameChatAsync(string ownerId, Guid chatId, string title, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<bool> DeleteChatAsync(string ownerId, Guid chatId, CancellationToken ct = default) =>
        Task.FromResult(true);
}

internal sealed class FakeChatClient : IChatClient
{
    // Defaults to a turn that answers and finishes, so a test that is not about
    // the model does not have to say anything about it.
    public Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> OnStream { get; set; } =
        _ => ModelStream.Answering("Hei");

    // What the last turn actually asked for. The system prompt no longer travels
    // in the message list, so the only way to assert on it is to keep the options
    // the session built.
    public ChatOptions? LastOptions { get; private set; }

    public IReadOnlyList<AiMessage> LastMessages { get; private set; } = [];

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<AiMessage> messages,
        ChatOptions? options = null,
        CancellationToken ct = default)
    {
        // Materialised here: the session builds a fresh list per turn, but nothing
        // in the contract says a caller could not hand over a lazy sequence.
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

// Two models, both backed by the same fake client: these tests are about which
// model the session picks and records, never about a second provider behaving
// differently. A test that switches models asserts on the selection, not on a
// different answer coming back.
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

    // Every key a turn actually resolved, in order. Since all the models here
    // share one client, this is the only thing that can tell a test which model a
    // turn ran on.
    public List<ChatModelKey> ResolvedKeys { get; } = [];

    public IReadOnlyList<ChatModel> ModelsFor(ClaimsPrincipal user) => _models;

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

// The model's side of a turn, in the three shapes these tests need.
internal static class ModelStream
{
    public static async IAsyncEnumerable<ChatResponseUpdate> Answering(string text)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, text);
        await Task.CompletedTask;
    }

    // Usage arrives as its own update, the way a provider sends it: a trailing
    // chunk after the text. ToChatResponse folds it into ChatResponse.Usage.
    public static async IAsyncEnumerable<ChatResponseUpdate> AnsweringWithUsage(string text, long inputTokens, long outputTokens)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, text);
        yield return new ChatResponseUpdate(ChatRole.Assistant,
        [
            new UsageContent(new UsageDetails
            {
                InputTokenCount = inputTokens,
                OutputTokenCount = outputTokens,
                TotalTokenCount = inputTokens + outputTokens
            })
        ]);
        await Task.CompletedTask;
    }

    // Breaks partway, which is the realistic shape: tokens have already reached
    // the browser by the time the provider gives up.
    public static async IAsyncEnumerable<ChatResponseUpdate> FailingAfter(string text, Exception failure)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, text);
        await Task.Yield();
        throw failure;
    }

    // Produces nothing and never finishes, so a test can cancel a turn that is
    // genuinely in flight rather than one that has already completed.
    public static async IAsyncEnumerable<ChatResponseUpdate> Stalling(
        TaskCompletionSource reached,
        [EnumeratorCancellation] CancellationToken ct)
    {
        reached.TrySetResult();
        await Task.Delay(Timeout.Infinite, ct);
        yield break;
    }
}

// Records every metric the session emits, so a test can assert on what ops would
// actually see — which counter, carrying which labels — rather than on the mere
// fact that something was counted.
internal sealed class RecordingMetricsService : IMetricsService
{
    public List<MetricCall> Calls { get; } = [];

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

    private void Record(string name, (string, string)[] labels) =>
        Calls.Add(new MetricCall(name, labels.ToDictionary(label => label.Item1, label => label.Item2)));

    // Nothing here asserts on durations, so the timer always reports none.
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

// The session pushes tokens at a browser. There isn't one here, and there is
// nothing to assert about it: FlushCadence already owns what gets sent, and
// ChatClientChannel already swallows what cannot be delivered.
internal sealed class SilentJsRuntime : IJSRuntime
{
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => default;

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args) => default;
}
