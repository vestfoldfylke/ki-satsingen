using kisatsingen.Services.Chat;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// The channel used to be too thin to test. It is not any more: it decides when
// to stop pushing at a browser that has gone quiet and when to start again, and
// getting the second half wrong is invisible — the circuit reconnects, the user
// sees nothing stream, and no error is raised anywhere.
public sealed class ChatClientChannelTests
{
    private static readonly Guid StreamId = Guid.NewGuid();

    [Fact]
    public async Task Tokens_reach_a_browser_that_is_listening()
    {
        var js = new RecordingJsRuntime();
        var channel = Build(js);

        channel.StreamAppend(StreamId, "Hei");
        await channel.Pending;

        Assert.Equal(["chatClient.streamAppend"], js.Calls);
    }

    [Fact]
    public async Task A_paused_channel_stops_pushing_at_a_browser_that_cannot_receive()
    {
        var js = new RecordingJsRuntime();
        var channel = Build(js);

        channel.Pause();
        channel.StreamAppend(StreamId, "unsent");
        await channel.Pending;

        Assert.Empty(js.Calls);
    }

    // The bug this file exists for. The pause used to be set by a failed interop
    // call and never lifted, so one blip left the channel mute for the rest of the
    // session and every later answer arrived in a single lump when it committed.
    [Fact]
    public async Task A_resumed_channel_starts_delivering_again()
    {
        var js = new RecordingJsRuntime();
        var channel = Build(js);

        channel.Pause();
        channel.StreamAppend(StreamId, "unsent");
        channel.Resume();
        channel.StreamAppend(StreamId, "delivered");
        await channel.Pending;

        Assert.Equal(["chatClient.streamAppend"], js.Calls);
    }

    // On an unclean drop, interop calls queue rather than throw and only fault
    // once SignalR gives up — which can be after the client has already come
    // back. If a failure like that could pause the channel, a reconnected user
    // would be muted for the rest of the session with no Resume left to save
    // them. Only the connection callbacks may set the pause.
    [Fact]
    public async Task A_failure_landing_after_a_resume_cannot_mute_the_channel()
    {
        // Held open so the call dispatched before the drop is still in flight
        // when the client reconnects, and fails only afterwards.
        var stillInFlight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var js = new RecordingJsRuntime { IsDisconnected = true, FailsWhenReleased = stillInFlight };
        var channel = Build(js);

        channel.StreamAppend(StreamId, "in flight when the transport dropped");
        var stale = channel.Pending;
        channel.Pause();
        channel.Resume();
        js.IsDisconnected = false;
        stillInFlight.SetResult();
        await stale;
        channel.StreamAppend(StreamId, "delivered");
        await channel.Pending;

        Assert.Equal(2, js.Calls.Count);
    }

    // A turn outlives a dropped transport: Blazor retains the circuit so the
    // client can return to it. Delivery failing must not surface as anything the
    // turn has to handle.
    [Fact]
    public async Task Undeliverable_tokens_never_fault_the_caller()
    {
        var js = new RecordingJsRuntime { IsDisconnected = true };
        var channel = Build(js);

        var failure = Record.Exception(() => channel.StreamAppend(StreamId, "Hei"));
        var pending = await Record.ExceptionAsync(() => channel.Pending);

        Assert.Null(failure);
        Assert.Null(pending);
    }

    // Interop can fail for reasons that say nothing about the transport. Those
    // must not stop delivery, or one bad call mutes a live circuit.
    [Fact]
    public async Task An_unrelated_interop_failure_does_not_stop_delivery()
    {
        var js = new RecordingJsRuntime { Failure = new InvalidOperationException("bad argument") };
        var channel = Build(js);

        channel.StreamAppend(StreamId, "first");
        await channel.Pending;
        js.Failure = null;
        channel.StreamAppend(StreamId, "second");
        await channel.Pending;

        Assert.Equal(2, js.Calls.Count);
    }

    private static ChatClientChannel Build(IJSRuntime js) => new(js, NullLogger.Instance);

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public List<string> Calls { get; } = [];

        // Stands in for a browser that has stopped listening. Blazor reports this
        // as JSDisconnectedException on every interop attempt.
        public bool IsDisconnected { get; set; }

        // When set, a disconnected call stays pending until this completes,
        // which is what lets a test place the failure after a reconnect.
        public TaskCompletionSource? FailsWhenReleased { get; set; }

        public Exception? Failure { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
        {
            Calls.Add(identifier);

            if (IsDisconnected)
            {
                return Disconnect<TValue>();
            }

            return Failure is not null
                ? ValueTask.FromException<TValue>(Failure)
                : default;
        }

        private async ValueTask<TValue> Disconnect<TValue>()
        {
            if (FailsWhenReleased is not null)
            {
                await FailsWhenReleased.Task;
            }

            throw new JSDisconnectedException("circuit gone");
        }
    }
}
