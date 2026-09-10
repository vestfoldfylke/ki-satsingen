using Microsoft.JSInterop;

namespace kisatsingen.Services.Chat;

// Pushes streaming tokens at a browser that may already be gone. Every call is
// fire-and-forget by design: a token that cannot be delivered must not fail the
// turn that produced it, and must not block the server on a dead circuit.
//
// Deliberately thin. It holds no transcript state and makes no decisions — what
// to send is FlushCadence's job, when to start and stop is ChatSession's — so
// there is nothing here worth testing beyond the interop calls themselves.
internal sealed class ChatClientChannel(IJSRuntime js, ILogger logger, Action onCircuitLost)
{
    private const int CircuitLive = 0;
    private const int CircuitLost = 1;

    private int _circuitState;

    public void StreamStart(Guid streamId) => Invoke("chatClient.streamStart", streamId);

    public void StreamAppend(Guid streamId, string chunk) => Invoke("chatClient.streamAppend", streamId, chunk);

    public void StreamEnd(Guid streamId) => Invoke("chatClient.streamEnd", streamId);

    private void Invoke(string method, params object?[] args)
    {
        if (Volatile.Read(ref _circuitState) == CircuitLost)
        {
            return;
        }

        _ = ObserveAsync();
        return;

        async Task ObserveAsync()
        {
            try
            {
                await js.InvokeVoidAsync(method, args);
            }
            catch (JSDisconnectedException)
            {
                // First observer of the disconnect tells the caller, so an
                // in-flight stream is cancelled once rather than per token.
                if (Interlocked.Exchange(ref _circuitState, CircuitLost) == CircuitLive)
                {
                    logger.LogInformation("Client circuit disconnected; cancelling active stream.");
                    onCircuitLost();
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "JS interop failed for {Method}", method);
            }
        }
    }
}
