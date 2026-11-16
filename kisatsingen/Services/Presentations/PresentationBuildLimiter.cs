using System.Diagnostics.CodeAnalysis;

namespace kisatsingen.Services.Presentations;

public sealed record PresentationBuildLimiterOptions(int MaxConcurrentBuilds, TimeSpan MaxWait)
{
    // How long a click waits for a free slot before the user is told to try again.
    public static readonly TimeSpan DefaultMaxWait = TimeSpan.FromSeconds(30);

    // One build per core: a build is CPU-bound, so more at once only adds memory
    // (each takes 15–20 MB while it runs) without finishing any sooner.
    public static PresentationBuildLimiterOptions Default { get; } = new(Environment.ProcessorCount, DefaultMaxWait);
}

public abstract record PresentationBuildResult
{
    private PresentationBuildResult() { }

    public sealed record Built(ArraySegment<byte> Content) : PresentationBuildResult;

    // Every slot stayed taken for MaxWait; nothing was built.
    public sealed record Busy : PresentationBuildResult;

    // The build threw. That is a template or OpenXml fault, not the user's;
    // the caller logs Error with what it knows about the request.
    public sealed record Failed(Exception Error) : PresentationBuildResult;
}

// Caps how many presentations are built at once, so many clicks at the same
// moment queue up instead of filling the heap. Waiting takes no thread.
// Not disposable: the semaphore holds no wait handle unless one is asked for,
// and disposing it on shutdown would make a build still running throw from
// its Release.
[SuppressMessage("Reliability", "CA1001", Justification = "The semaphore's wait handle is never created; disposing it on shutdown would break builds still running.")]
public sealed class PresentationBuildLimiter(VestfoldPresentationBuilder builder, PresentationBuildLimiterOptions options)
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        // Out of memory is left to crash: it is the process failing, not the build.
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new PresentationBuildResult.Failed(ex);
        }
        finally
        {
            _slots.Release();
        }
    }
}
