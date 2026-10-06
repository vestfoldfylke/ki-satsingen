using kisatsingen.Services.Attachments;

namespace kisatsingen.Services.KnowledgeFiles.Processing;

// Runs queued jobs with a fixed number of workers. Every job ends with its
// Completion set, its temp file deleted and its owner's slot released, on
// every path: done, rejected, failed, timed out, cancelled, or still queued
// when the service stops.
public sealed class KnowledgeFileProcessingWorker(
    KnowledgeFileProcessingQueue queue,
    KnowledgeFileProcessor processor,
    TempFileStore store,
    KnowledgeFileProcessingOptions options,
    KnowledgeFileProcessingMetrics metrics,
    ILogger<KnowledgeFileProcessingWorker> logger) : BackgroundService
{
    private enum Outcome
    {
        Processed,
        Rejected,
        TimedOut,
        Cancelled,
        ShuttingDown,
        Failed
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.WhenAll(Enumerable.Range(0, options.Workers).Select(_ => RunAsync(stoppingToken)));
        }
        finally
        {
            queue.Close();
            DrainUnstarted();
        }
    }

    // A stop that lands before the thread pool gets to ExecuteAsync skips it
    // entirely, finally block included, and the queue would stay open with
    // nobody reading it.
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        queue.Close();
        DrainUnstarted();
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
            {
                await RunJobAsync(job, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    // Never throws: one bad file must not take a worker down with it. Hence
    // everything, setup included, inside the try.
    private async Task RunJobAsync(ProcessingJob job, CancellationToken stoppingToken)
    {
        var outcome = Outcome.Failed;
        CancellationTokenSource? timeout = null;
        CancellationTokenSource? linked = null;

        try
        {
            metrics.Observe(job.QueueWait);

            // Stopped while it waited: nobody is waiting for the result.
            if (job.Cancellation.IsCancellationRequested)
            {
                outcome = Outcome.Cancelled;
                job.Completion.TrySetCanceled(job.Cancellation);
                return;
            }

            timeout = new CancellationTokenSource(options.JobTimeout);
            linked = CancellationTokenSource.CreateLinkedTokenSource(job.Cancellation, stoppingToken, timeout.Token);
            var conversion = metrics.StartConversion();

            var result = await processor.ProcessAsync(job.Attachment, job.Progress, linked.Token);
            outcome = result is KnowledgeFileProcessingResult.Processed ? Outcome.Processed : Outcome.Rejected;
            job.Completion.TrySetResult(result);
            metrics.Observe(conversion);
        }
        // The order matters: the caller's own stop is a cancellation, the
        // service's is a refusal the user can retry, and only the timer left
        // is the timeout.
        catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested)
        {
            outcome = Outcome.Cancelled;
            job.Completion.TrySetCanceled(job.Cancellation);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            outcome = Outcome.ShuttingDown;
            job.Completion.TrySetResult(new KnowledgeFileProcessingResult.Rejected(ProcessingRejections.ShuttingDown));
        }
        catch (OperationCanceledException) when (timeout?.IsCancellationRequested == true)
        {
            outcome = Outcome.TimedOut;
            logger.LogWarning("Processing upload {UploadId} ({ContentType}) timed out after {Timeout}.", job.Attachment.UploadId, job.Attachment.Type.ContentType, options.JobTimeout);
            job.Completion.TrySetResult(new KnowledgeFileProcessingResult.Rejected(ProcessingRejections.TimedOut));
        }
        catch (Exception ex)
        {
            outcome = Outcome.Failed;
            logger.LogError(ex, "Processing upload {UploadId} ({ContentType}, {SizeBytes} bytes) failed.", job.Attachment.UploadId, job.Attachment.Type.ContentType, job.Attachment.SizeBytes);
            job.Completion.TrySetException(ex);
        }
        finally
        {
            linked?.Dispose();
            timeout?.Dispose();
            Release(job);
            metrics.CountOutcome(job.Attachment.Type.ContentType, outcome.ToString());
        }
    }

    // Jobs still queued when the workers stopped: without this their callers
    // would wait forever and their temp files would wait for the next startup.
    private void DrainUnstarted()
    {
        while (queue.Reader.TryRead(out var job))
        {
            job.Completion.TrySetResult(new KnowledgeFileProcessingResult.Rejected(ProcessingRejections.ShuttingDown));
            Release(job);
            metrics.CountOutcome(job.Attachment.Type.ContentType, nameof(Outcome.ShuttingDown));
        }
    }

    private void Release(ProcessingJob job)
    {
        store.Delete(job.Attachment.File);
        job.OwnerSlot.Dispose();
    }
}
