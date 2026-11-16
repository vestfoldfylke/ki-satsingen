namespace kisatsingen.Services.Presentations;

public sealed record PresentationBuildLimiterOptions(int MaxConcurrentBuilds, TimeSpan MaxWait)
{
    // One build per core: a build is CPU-bound, so more at once only adds memory
    // (each takes 15–20 MB while it runs) without finishing any sooner.
    public static PresentationBuildLimiterOptions Default { get; } = new(Environment.ProcessorCount, TimeSpan.FromSeconds(30));
}

public abstract record PresentationBuildResult
{
    private PresentationBuildResult() { }

    public sealed record Built(byte[] Content) : PresentationBuildResult;

    // Every slot stayed taken for MaxWait; nothing was built.
    public sealed record Busy : PresentationBuildResult;
}

// Caps how many presentations are built at once, so many clicks at the same
// moment queue up instead of filling the heap. Waiting takes no thread.
public sealed class PresentationBuildLimiter(VestfoldPresentationBuilder builder, PresentationBuildLimiterOptions options) : IDisposable
{
    private readonly SemaphoreSlim _slots = new(options.MaxConcurrentBuilds, options.MaxConcurrentBuilds);

    public async Task<PresentationBuildResult> BuildAsync(PresentationSpec spec, CancellationToken ct = default)
    {
        if (!await _slots.WaitAsync(options.MaxWait, ct))
        {
            return new PresentationBuildResult.Busy();
        }

        try
        {
            // Off the caller's thread, so a Blazor circuit can render its busy
            // state and stay responsive while the file is built.
            return new PresentationBuildResult.Built(await Task.Run(() => builder.Build(spec), ct));
        }
        finally
        {
            _slots.Release();
        }
    }

    public void Dispose() => _slots.Dispose();
}
