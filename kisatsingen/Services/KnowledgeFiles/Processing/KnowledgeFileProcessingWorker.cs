using kisatsingen.Constants;
using kisatsingen.Services.Attachments;
using Vestfold.Extensions.Metrics.Services;

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
    IMetricsService metrics,
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

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
            {
                job.QueueWait.ObserveDuration();
                await RunJobAsync(job, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    // Never throws: one bad file must not take a worker down with it.
    private async Task RunJobAsync(ProcessingJob job, CancellationToken stoppingToken)
    {
        using var timeout = new CancellationTokenSource(options.JobTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(job.Cancellation, stoppingToken, timeout.Token);
        var duration = metrics.Histogram($"{KnowledgeFileProcessingQueue.MetricPrefix}_Duration", "Time spent converting a knowledge file");
        var outcome = Outcome.Failed;

        try
        {
            // Stopped while it waited: nobody is waiting for the result.
            if (job.Cancellation.IsCancellationRequested)
            {
                outcome = Outcome.Cancelled;
                job.Completion.TrySetCanceled(job.Cancellation);
                return;
            }

            var result = await processor.ProcessAsync(job.Attachment, job.Progress, linked.Token);
            outcome = result is ProcessingResult.Processed ? Outcome.Processed : Outcome.Rejected;
            job.Completion.TrySetResult(result);
            duration.ObserveDuration();
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
            job.Completion.TrySetResult(new ProcessingResult.Rejected(ProcessingRejections.ShuttingDown));
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            outcome = Outcome.TimedOut;
            logger.LogWarning("Processing upload {UploadId} ({ContentType}) timed out after {Timeout}.", job.Attachment.UploadId, job.Attachment.Type.ContentType, options.JobTimeout);
            job.Completion.TrySetResult(new ProcessingResult.Rejected(ProcessingRejections.TimedOut));
        }
        catch (Exception ex)
        {
            outcome = Outcome.Failed;
            logger.LogError(ex, "Processing upload {UploadId} ({ContentType}, {SizeBytes} bytes) failed.", job.Attachment.UploadId, job.Attachment.Type.ContentType, job.Attachment.SizeBytes);
            job.Completion.TrySetException(ex);
        }
        finally
        {
            Release(job);
            CountOutcome(job, outcome);
        }
    }

    // Jobs still queued when the workers stopped: without this their callers
    // would wait forever and their temp files would wait for the next startup.
    private void DrainUnstarted()
    {
        while (queue.Reader.TryRead(out var job))
        {
            job.Completion.TrySetResult(new ProcessingResult.Rejected(ProcessingRejections.ShuttingDown));
            Release(job);
            CountOutcome(job, Outcome.ShuttingDown);
        }
    }

    private void Release(ProcessingJob job)
    {
        store.Delete(job.Attachment.File);
        job.OwnerSlot.Dispose();
    }

    // Guarded: Prometheus throws on a label mismatch, and that must never
    // replace a job's own outcome.
    private void CountOutcome(ProcessingJob job, Outcome outcome)
    {
        try
        {
            metrics.Count(
                $"{KnowledgeFileProcessingQueue.MetricPrefix}_Processed",
                "Knowledge files processed, by content type and outcome",
                (MetricConstants.MetricsContentTypeLabelName, job.Attachment.Type.ContentType),
                (MetricConstants.MetricsResultLabelName, outcome.ToString()));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not count the {Outcome} outcome for upload {UploadId}.", outcome, job.Attachment.UploadId);
        }
    }
}
