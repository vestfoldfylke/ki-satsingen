using System.Security.Cryptography;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using kisatsingen.Services.KnowledgeFiles.Processing;
using kisatsingen.Tests.Services.Attachments;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Processing;

// Holds every conversion until released, so a test can act while a job is
// genuinely running or genuinely queued behind one.
internal sealed class StallingConverter : IDocumentConverter
{
    public static readonly AttachmentType Type = new("test/stall", Signature: null);

    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _started = new(0);
    private int _startedCount;

    public IReadOnlyList<string> ContentTypes { get; } = [Type.ContentType];

    public int StartedCount => Volatile.Read(ref _startedCount);

    public void Release() => _release.TrySetResult();

    // Fails the test rather than hanging it when a conversion never starts.
    public async Task WaitUntilStartedAsync()
    {
        if (!await _started.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("No conversion started within 5 seconds.");
        }
    }

    public async Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref _startedCount);
        _started.Release();
        await _release.Task.WaitAsync(ct);
        return new ConversionResult.Converted("# Ferdig\n\nInnhold.", ContentOrigin.TextFile, PageCount: null);
    }
}

// A bug in a converter, as opposed to a file it refuses.
internal sealed class ThrowingConverter : IDocumentConverter
{
    public static readonly AttachmentType Type = new("test/throw", Signature: null);

    public IReadOnlyList<string> ContentTypes { get; } = [Type.ContentType];

    public Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken ct) =>
        throw new InvalidOperationException("Converter bug.");
}

internal static class ReadyAttachments
{
    // A real temp file in the environment's store, so tests can see it deleted.
    public static ReadyAttachment From(AttachmentTestEnvironment environment, byte[] content, AttachmentType type, string fileName = "fil.md")
    {
        var (file, stream) = environment.Store.Create();
        using (stream)
        {
            stream.Write(content);
        }

        return new ReadyAttachment(Guid.NewGuid(), fileName, type, content.Length, Convert.ToHexStringLower(SHA256.HashData(content)), file);
    }
}

internal sealed class RecordingProgress : IProgress<KnowledgeFileProcessingStage>
{
    public List<KnowledgeFileProcessingStage> Reported { get; } = [];

    public void Report(KnowledgeFileProcessingStage value)
    {
        lock (Reported)
        {
            Reported.Add(value);
        }
    }
}

internal static class Eventually
{
    // For effects a worker finishes after the caller has already returned.
    public static async Task TrueAsync(Func<bool> condition, string failure)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(failure);
            }

            await Task.Delay(10);
        }
    }
}

// Every call throws, the way Prometheus does on a registration conflict.
internal sealed class BrokenMetricsService : Vestfold.Extensions.Metrics.Services.IMetricsService
{
    private static InvalidOperationException Broken() => new("Metrics are broken.");

    public void Count(string name, string? description, int value = 1) => throw Broken();
    public void Count(string name, string? description, params (string, string)[] labels) => throw Broken();
    public void Count(string name, string? description, int value, params (string, string)[] labels) => throw Broken();
    public void Gauge(string name, double value) => throw Broken();
    public void Gauge(string name, string description, double value) => throw Broken();
    public void Gauge(string name, double value, params (string, string)[] labels) => throw Broken();
    public void Gauge(string name, string description, double value, params (string, string)[] labels) => throw Broken();
    public Prometheus.ITimer Histogram(string name, string? description) => throw Broken();
    public Prometheus.ITimer Histogram(string name, params (string, string)[] labels) => throw Broken();
    public Prometheus.ITimer Histogram(string name, string? description, params (string, string)[] labels) => throw Broken();
}
