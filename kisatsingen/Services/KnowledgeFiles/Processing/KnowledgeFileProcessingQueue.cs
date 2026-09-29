using System.Threading.Channels;
using kisatsingen.Services.Attachments;

namespace kisatsingen.Services.KnowledgeFiles.Processing;

// Where callers hand attachments in and await their results. Bounded, and a
// full queue makes the caller wait rather than fail: the wait shows in the
// turn, a failure would lose the file.
public sealed class KnowledgeFileProcessingQueue
{
    private readonly Channel<ProcessingJob> _channel;
    private readonly OwnerJobLimiter _limiter;
    private readonly TempFileStore _store;
    private readonly KnowledgeFileProcessingMetrics _metrics;

    public KnowledgeFileProcessingQueue(KnowledgeFileProcessingOptions options, TempFileStore store, KnowledgeFileProcessingMetrics metrics)
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
    public async Task<KnowledgeFileProcessingResult> ProcessAsync(
        string ownerId,
        ReadyAttachment attachment,
        IProgress<KnowledgeFileProcessingStage> progress,
        CancellationToken ct)
    {
        IDisposable? ownerSlot = null;
        var isHandedOver = false;

        try
        {
            progress.Report(KnowledgeFileProcessingStage.Queued);
            ownerSlot = await _limiter.AcquireAsync(ownerId, ct);

            var job = new ProcessingJob(attachment, progress, ownerSlot, _metrics.StartQueueWait(), ct);
            await _channel.Writer.WriteAsync(job, ct);
            isHandedOver = true;

            // Its own token too, so a stop ends the wait even if a worker hangs.
            return await job.Completion.Task.WaitAsync(ct);
        }
        catch (ChannelClosedException)
        {
            return new KnowledgeFileProcessingResult.Rejected(ProcessingRejections.ShuttingDown);
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
