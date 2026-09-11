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

    // Every push after the transport drops would throw the same exception. One
    // failed call is enough to know the browser is not there.
    [Fact]
    public async Task A_dropped_transport_stops_further_pushes()
    {
        var js = new RecordingJsRuntime { IsDisconnected = true };
        var channel = Build(js);

        channel.StreamAppend(StreamId, "first");
        await channel.Pending;
        channel.StreamAppend(StreamId, "second");
        await channel.Pending;

        Assert.Single(js.Calls);
    }

    // The bug this file exists for. The circuit-lost flag used to latch with no
    // way back, so one blip left the channel mute for the rest of the session and
    // every later answer arrived in a single lump when it committed.
    [Fact]
    public async Task A_restored_transport_starts_delivering_again()
    {
        var js = new RecordingJsRuntime { IsDisconnected = true };
        var channel = Build(js);

        channel.StreamAppend(StreamId, "lost");
        await channel.Pending;
        js.IsDisconnected = false;
        channel.Restore();
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
    // must not pause delivery, or one bad call mutes a live circuit.
    [Fact]
    public async Task An_unrelated_interop_failure_does_not_pause_delivery()
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

        public Exception? Failure { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
        {
            Calls.Add(identifier);

            if (IsDisconnected)
            {
                throw new JSDisconnectedException("circuit gone");
            }

            return Failure is not null
                ? ValueTask.FromException<TValue>(Failure)
                : default;
        }
    }
}
