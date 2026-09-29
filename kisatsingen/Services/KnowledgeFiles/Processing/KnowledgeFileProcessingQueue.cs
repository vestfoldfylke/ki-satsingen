using System.Threading.Channels;
using kisatsingen.Constants;
using kisatsingen.Services.Attachments;
using Vestfold.Extensions.Metrics.Services;

namespace kisatsingen.Services.KnowledgeFiles.Processing;

// Where callers hand attachments in and await their results. Bounded, and a
// full queue makes the caller wait rather than fail: the wait shows in the
// turn, a failure would lose the file.
public sealed class KnowledgeFileProcessingQueue
{
    internal static readonly string MetricPrefix = $"{MetricConstants.MetricsAppPrefix}_KnowledgeFile";

    private readonly Channel<ProcessingJob> _channel;
    private readonly OwnerJobLimiter _limiter;
    private readonly TempFileStore _store;
    private readonly IMetricsService _metrics;

    public KnowledgeFileProcessingQueue(KnowledgeFileProcessingOptions options, TempFileStore store, IMetricsService metrics)
    {
        _channel = Channel.CreateBounded<ProcessingJob>(new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
        _limiter = new OwnerJobLimiter(options.MaxActiveJobsPerUser);
        _store = store;
        _metrics = metrics;
    }

    internal ChannelReader<ProcessingJob> Reader => _channel.Reader;

    // Takes ownership of the attachment's temp file: it is deleted whatever
    // happens, whether or not the job ever reached a worker.
    public async Task<ProcessingResult> ProcessAsync(
        string ownerId,
        ReadyAttachment attachment,
        IProgress<ProcessingStage> progress,
        CancellationToken ct)
    {
        IDisposable? ownerSlot = null;
        var isHandedOver = false;

        try
        {
            progress.Report(ProcessingStage.Queued);
            ownerSlot = await _limiter.AcquireAsync(ownerId, ct);

            var queueWait = _metrics.Histogram($"{MetricPrefix}_QueueWait", "Time a knowledge file waited for a worker");
            var job = new ProcessingJob(attachment, progress, ownerSlot, queueWait, ct);
            await _channel.Writer.WriteAsync(job, ct);
            isHandedOver = true;

            // Its own token too, so a stop ends the wait even if a worker hangs.
            return await job.Completion.Task.WaitAsync(ct);
        }
        catch (ChannelClosedException)
        {
            return new ProcessingResult.Rejected(ProcessingRejections.ShuttingDown);
        }
        finally
        {
            // From here on the worker owns both; before it, nobody else does.
            if (!isHandedOver)
            {
                ownerSlot?.Dispose();
                _store.Delete(attachment.File);
            }
        }
    }

    // Called once by the worker as it stops: later ProcessAsync calls are
    // refused, and what is still queued is drained by the worker.
    internal void Close() => _channel.Writer.TryComplete();
}
