using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services;
using kisatsingen.Services.Chat;
using System.Runtime.CompilerServices;
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
    public RecordingMetricsService Metrics { get; } = new();
    public ChatSession Session { get; }

    public ChatSessionHarness() =>
        Session = new ChatSession(
            Authentication,
            Client,
            Repository,
            Metrics,
            new SilentJsRuntime(),
            NullLogger<ChatSession>.Instance);

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

    // Indexed by call, because one turn appends twice — the user's message, then
    // the model's reply — and "the answer could not be saved" is precisely the
    // case where only the second one fails.
    public Exception? FirstAppendMessagesFailure { get; set; }
    public Exception? SecondAppendMessagesFailure { get; set; }

    private int _appendMessagesCalls;

    public Task<ChatEntity> CreateChatAsync(string ownerId, string title, CancellationToken ct = default) =>
        CreateChatFailure is not null
            ? Task.FromException<ChatEntity>(CreateChatFailure)
            : Task.FromResult(new ChatEntity
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                Title = title,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

    public Task<ChatEntity?> GetChatAsync(string ownerId, Guid chatId, CancellationToken ct = default) =>
        Task.FromResult<ChatEntity?>(null);

    public Task<IReadOnlyList<ChatSummary>> ListChatsAsync(string ownerId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ChatSummary>>([]);

    public Task AppendMessagesAsync(string ownerId, Guid chatId, IReadOnlyList<StoredMessage> messages, CancellationToken ct = default)
    {
        var failure = ++_appendMessagesCalls == 1 ? FirstAppendMessagesFailure : SecondAppendMessagesFailure;
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
}

internal sealed class FakeChatClient : IChatClient
{
    // Defaults to a turn that answers and finishes, so a test that is not about
    // the model does not have to say anything about it.
    public Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> OnStream { get; set; } =
        _ => ModelStream.Answering("Hei");

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<AiMessage> messages,
        ChatOptions? options = null,
        CancellationToken ct = default) => OnStream(ct);

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

// The model's side of a turn, in the three shapes these tests need.
internal static class ModelStream
{
    public static async IAsyncEnumerable<ChatResponseUpdate> Answering(string text)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, text);
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
