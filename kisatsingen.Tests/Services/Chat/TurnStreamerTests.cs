using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// The contract: every text segment gets a render, so its element exists (in any
// order: the browser looks it up lazily), and a stream is dropped before the page
// is asked to render over it (in that order).
public sealed class TurnStreamerTests
{
    [Fact]
    public async Task Text_either_side_of_a_tool_streams_to_separate_browser_streams()
    {
        var (log, builder) = await Stream(ModelStream.UsingATool("før", "etter"));

        var answer = builder.Snapshot().Answer.OfType<TextSegment>().ToList();
        Assert.Equal([answer[0].Id, answer[1].Id], log.OfType<JsCall>().Where(call => call.Method == "chatClient.streamStart").Select(call => call.SegmentId));
    }

    [Fact]
    public async Task A_text_stream_is_ended_before_the_page_renders_the_tool_after_it()
    {
        var (log, builder) = await Stream(ModelStream.UsingATool("før", "etter"));

        var before = builder.Snapshot().Answer.OfType<TextSegment>().First().Id;
        var endOfText = log.FindIndex(entry => entry is JsCall { Method: "chatClient.streamEnd" } call && call.SegmentId == before);
        var firstToolRender = log.FindIndex(entry => entry is Render { Answer: [.., ToolCall] });
        Assert.InRange(endOfText, 0, firstToolRender - 1);
    }

    // Without it the stream would write into an element the page never renders.
    [Fact]
    public async Task Every_text_segment_asks_the_page_to_render_it()
    {
        var (log, builder) = await Stream(ModelStream.UsingATool("før", "etter"));

        var segmentIds = builder.Snapshot().Answer.OfType<TextSegment>().Select(text => text.Id);
        var renderedIds = log.OfType<Render>().SelectMany(render => render.Answer.OfType<TextSegment>()).Select(text => text.Id);
        Assert.Subset(renderedIds.ToHashSet(), segmentIds.ToHashSet());
    }

    // Shown once it ends, so the page renders it while it runs; the browser never streams it.
    [Fact]
    public async Task Reasoning_asks_the_page_to_render_it_and_opens_no_browser_stream()
    {
        var (log, builder) = await Stream(ReasoningThenAnswer());

        var reasoningId = builder.Snapshot().Answer.OfType<ReasoningSegment>().Single().Id;
        Assert.Contains(log, entry => entry is Render { Answer: [ReasoningSegment] });
        Assert.DoesNotContain(log.OfType<JsCall>(), call => call.SegmentId == reasoningId);
    }

    // The browser holds a buffer per stream, so a stopped turn must release it too.
    [Fact]
    public async Task A_stopped_stream_still_releases_the_browser_stream_it_was_writing()
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource();

        var streaming = Stream(ModelStream.AnsweringThenStalling("Hei", reached, stop.Token), stop.Token);
        await reached.Task;
        await stop.CancelAsync();
        var failure = await Record.ExceptionAsync(() => streaming);

        Assert.IsAssignableFrom<OperationCanceledException>(failure);
        Assert.Contains(Log, entry => entry is JsCall { Method: "chatClient.streamEnd" });
    }

    private List<object> Log { get; } = [];

    private static async IAsyncEnumerable<ChatResponseUpdate> ReasoningThenAnswer()
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("tenker")]);
        yield return new ChatResponseUpdate(ChatRole.Assistant, "svar");
        await Task.CompletedTask;
    }

    private async Task<(List<object> Log, TurnBuilder Builder)> Stream(
        IAsyncEnumerable<ChatResponseUpdate> updates,
        CancellationToken ct = default)
    {
        var builder = new TurnBuilder(Turn.Start("hei", ModelUnderTest, "be brief", []));
        var channel = new ChatClientChannel(new LoggingJsRuntime(Log), NullLogger.Instance);
        var streamer = new TurnStreamer(channel, new RecordingMetricsService(), "test");

        await streamer.StreamAsync(
            new ScriptedClient(updates),
            [],
            new ChatOptions(),
            builder,
            () => Log.Add(new Render(builder.Snapshot().Answer)),
            ct);

        return (Log, builder);
    }

    private static readonly ChatModel ModelUnderTest = new()
    {
        Key = FakeChatModelCatalog.DefaultKey,
        DisplayName = "Fast",
        ShortDescription = "short",
        LongDescription = "long",
        Provider = "test",
        ModelId = "wire-id",
        ContextWindowTokens = 128_000,
        IconName = "icon"
    };

    private sealed record Render(IReadOnlyList<TurnSegment> Answer);

    private sealed record JsCall(string Method, Guid SegmentId);

    private sealed class LoggingJsRuntime(List<object> log) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
        {
            log.Add(new JsCall(identifier, (Guid)args![0]!));
            return default;
        }
    }

    private sealed class ScriptedClient(IAsyncEnumerable<ChatResponseUpdate> updates) : IChatClient
    {
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken ct = default) => updates;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken ct = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
