using kisatsingen.Services.Attachments;
using MetricTimer = Prometheus.ITimer;

namespace kisatsingen.Services.KnowledgeFiles.Processing;

// One attachment on its way through the queue. Once written to the queue it
// owns the temp file and the owner's slot: the worker releases both on every
// path, and completes Completion on every path, so a caller never waits
// forever.
internal sealed class ProcessingJob(
    ReadyAttachment attachment,
    IProgress<KnowledgeFileProcessingStage> progress,
    IDisposable ownerSlot,
    MetricTimer? queueWait,
    CancellationToken cancellation)
{
    public ReadyAttachment Attachment { get; } = attachment;
    public IProgress<KnowledgeFileProcessingStage> Progress { get; } = progress;
    public IDisposable OwnerSlot { get; } = ownerSlot;

    // Started when the job was queued, observed when a worker takes it. Null
    // when the metric could not be started, which never stops the job.
    public MetricTimer? QueueWait { get; } = queueWait;

    public CancellationToken Cancellation { get; } = cancellation;

    // RunContinuationsAsynchronously, so completing it never runs the caller's
    // continuation (a whole chat turn) on the worker's thread.
    public TaskCompletionSource<KnowledgeFileProcessingResult> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
