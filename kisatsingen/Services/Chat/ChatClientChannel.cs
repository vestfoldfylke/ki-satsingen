using Microsoft.JSInterop;

namespace kisatsingen.Services.Chat;

// Pushes streaming tokens at a browser that may not be listening. Every call is
// fire-and-forget by design: a token that cannot be delivered must not fail the
// turn that produced it, and must not block the server on a dead circuit.
//
// A dropped transport is explicitly not the turn's problem. Blazor retains a
// disconnected circuit (3 minutes by default) so the client can come back to it,
// and the turn keeps running and persisting in the meantime. Delivery pauses
// while the transport is down, so a blip costs the user the tokens they could
// not have seen anyway — not the answer.
//
// Pause and Resume are the only writers of that flag, and they are driven by the
// circuit's own connection callbacks, which are ordered. Letting a failed interop
// call set it too would race: on an unclean drop, calls queue rather than throw
// and only fault once SignalR gives up, so a client that reconnects first would
// have its live channel muted by a pile of stale failures — with no further
// Resume coming to undo it.
internal sealed class ChatClientChannel(IJSRuntime js, ILogger logger)
{
    // Volatile here => A stale read costs one token either way — already tolerated. Interlocked
    // would fence every token for no gain.
    private volatile bool _isPaused;

    // The transport is down. Stops queueing calls the client cannot receive.
    public void Pause() => _isPaused = true;

    // The transport came back. Without this the pause would latch and every
    // later turn on this circuit would silently stream nothing.
    public void Resume() => _isPaused = false;

    // StreamStart/Append/End return Task as a test seam so a specific dispatch
    // can be awaited. Production discards with `_ =`; ObserveAsync catches every
    // interop failure below, so a discarded task terminates cleanly and there is
    // nothing left to observe.
    public Task StreamStart(Guid streamId) => Invoke("chatClient.streamStart", streamId);

    public Task StreamAppend(Guid streamId, string chunk) => Invoke("chatClient.streamAppend", streamId, chunk);

    public Task StreamEnd(Guid streamId) => Invoke("chatClient.streamEnd", streamId);

    private Task Invoke(string method, params object?[] args)
    {
        if (_isPaused)
        {
            return Task.CompletedTask;
        }

        return ObserveAsync();

        async Task ObserveAsync()
        {
            try
            {
                await js.InvokeVoidAsync(method, args);
            }
            catch (JSDisconnectedException)
            {
                // Swallowed, and deliberately without touching _isPaused: the
                // circuit's connection callbacks own that flag, and this failure
                // may be a stale one landing after a reconnect. The drop itself is
                // already recorded by BlazorCircuitObserver.
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "JS interop failed for {Method}", method);
            }
        }
    }
}
