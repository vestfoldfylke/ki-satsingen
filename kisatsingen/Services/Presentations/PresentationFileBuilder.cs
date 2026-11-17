namespace kisatsingen.Services.Presentations;

// The presentations built for one tab's download buttons. Scoped, so it lives
// as long as the circuit: opening another chat leaves a build running, and the
// file still arrives, while closing the tab gives up the build's place in the
// queue that the singleton PresentationBuildLimiter keeps for everyone.
public sealed class PresentationFileBuilder(PresentationBuildLimiter limiter) : IDisposable
{
    // Cancelled, never disposed: a build may still be waiting on its token, and
    // a source without a timer holds nothing that needs releasing.
    private readonly CancellationTokenSource _cancellation = new();

    // Whether the circuit has ended. Lets a caller tell this cancellation apart
    // from an OperationCanceledException that came from somewhere else.
    public bool IsCancelled => _cancellation.IsCancellationRequested;

    // Throws OperationCanceledException when the circuit ends first.
    public Task<PresentationBuildResult> BuildAsync(PresentationSpec spec) =>
        limiter.BuildAsync(spec, _cancellation.Token);

    public void Dispose() => _cancellation.Cancel();
}
