using Microsoft.JSInterop;

namespace kisatsingen.Services.Chat;

// Pushes streaming tokens at a browser that may not be listening. Every call is
// fire-and-forget by design: a token that cannot be delivered must not fail the
// turn that produced it, and must not block the server on a dead circuit.
//
// A dropped transport is explicitly not the turn's problem. Blazor retains a
// disconnected circuit (3 minutes by default) so the client can come back to it,
// and the turn keeps running and persisting in the meantime. This stops pushing
// while the transport is down and starts again on Restore, so a blip costs the
// user the tokens they could not have seen anyway — not the answer.
internal sealed class ChatClientChannel(IJSRuntime js, ILogger logger)
{
    private const int CircuitLive = 0;
    private const int CircuitLost = 1;

    private int _circuitState;

    // The interop call in flight. Production discards it — that is the whole
    // point of this type — but the suppressed state transitions below are worth
    // asserting on, and a test has nothing else to await.
    internal Task Pending { get; private set; } = Task.CompletedTask;

    // The transport came back. Without this the circuit-lost flag latches and
    // every later turn on this circuit silently streams nothing.
    public void Restore() => Volatile.Write(ref _circuitState, CircuitLive);

    public void StreamStart(Guid streamId) => Invoke("chatClient.streamStart", streamId);

    public void StreamAppend(Guid streamId, string chunk) => Invoke("chatClient.streamAppend", streamId, chunk);

    public void StreamEnd(Guid streamId) => Invoke("chatClient.streamEnd", streamId);

    private void Invoke(string method, params object?[] args)
    {
        if (Volatile.Read(ref _circuitState) == CircuitLost)
        {
            return;
        }

        Pending = ObserveAsync();
        return;

        async Task ObserveAsync()
        {
            try
            {
                await js.InvokeVoidAsync(method, args);
            }
            catch (JSDisconnectedException)
            {
                // Stop pushing until the transport returns. Logged by the first
                // observer only, so a dropped connection is one line rather than
                // one per token.
                if (Interlocked.Exchange(ref _circuitState, CircuitLost) == CircuitLive)
                {
                    logger.LogInformation("Client transport lost; pausing stream delivery until it returns.");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "JS interop failed for {Method}", method);
            }
        }
    }
}
