using System.Text;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using kisatsingen.Services.KnowledgeFiles.Processing;
using kisatsingen.Tests.Services.Attachments;
using kisatsingen.Tests.Services.Chat;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Processing;

// The real queue, worker and processor; only the converters are doubles, so a
// test can hold a job running or make it fail.
public sealed class KnowledgeFileProcessingQueueTests : IAsyncLifetime
{
    private const string Owner = "owner";
    private const string OtherOwner = "someone-else";

    private readonly AttachmentTestEnvironment _environment = new();
    private readonly StallingConverter _stalling = new();
    private KnowledgeFileProcessingQueue _queue = null!;
    private KnowledgeFileProcessingWorker _worker = null!;

    public Task InitializeAsync() => StartAsync(new KnowledgeFileProcessingOptions { Workers = 1 });

    public async Task DisposeAsync()
    {
        _stalling.Release();
        await _worker.StopAsync(CancellationToken.None);
        _worker.Dispose();
        _environment.Dispose();
    }

    // Replaces the default pipeline for tests that need other limits.
    private async Task StartAsync(KnowledgeFileProcessingOptions options)
    {
        var metrics = new RecordingMetricsService();
        var processor = new KnowledgeFileProcessor(
            new DocumentConverterRegistry([new TextDocumentConverter(), _stalling, new ThrowingConverter()]),
            new OpeningExcerptSummarizer(),
            _environment.Store,
            maxEstimatedTokens: 1_000_000,
            NullLogger<KnowledgeFileProcessor>.Instance);

        _queue = new KnowledgeFileProcessingQueue(options, _environment.Store, metrics);
        _worker = new KnowledgeFileProcessingWorker(_queue, processor, _environment.Store, options, metrics, NullLogger<KnowledgeFileProcessingWorker>.Instance);
        await _worker.StartAsync(CancellationToken.None);
    }

    private async Task RestartAsync(KnowledgeFileProcessingOptions options)
    {
        await _worker.StopAsync(CancellationToken.None);
        _worker.Dispose();
        await StartAsync(options);
    }

    private ReadyAttachment Markdown() =>
        ReadyAttachments.From(_environment, Encoding.UTF8.GetBytes("# Tittel\n\nTekst."), AttachmentContentTypes.Markdown);

    private ReadyAttachment Stalling() => ReadyAttachments.From(_environment, [1], StallingConverter.Type);

    private Task<ProcessingResult> ProcessAsync(ReadyAttachment attachment, string owner = Owner, CancellationToken ct = default) =>
        _queue.ProcessAsync(owner, attachment, new RecordingProgress(), ct);

    [Fact]
    public async Task A_queued_attachment_comes_back_as_a_draft()
    {
        var result = await ProcessAsync(Markdown());

        Assert.IsType<ProcessingResult.Processed>(result);
    }

    [Fact]
    public async Task The_temp_file_is_deleted_once_its_job_is_done()
    {
        var attachment = Markdown();

        await ProcessAsync(attachment);

        await Eventually.TrueAsync(() => !File.Exists(attachment.File.Path), "The temp file outlived its job.");
    }

    [Fact]
    public async Task A_converter_bug_reaches_the_caller_as_an_exception_and_still_deletes_the_file()
    {
        var attachment = ReadyAttachments.From(_environment, [1], ThrowingConverter.Type);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessAsync(attachment));

        await Eventually.TrueAsync(() => !File.Exists(attachment.File.Path), "A failed job left its temp file behind.");
    }

    [Fact]
    public async Task A_job_that_runs_past_the_timeout_is_refused_with_a_reason()
    {
        await RestartAsync(new KnowledgeFileProcessingOptions { Workers = 1, JobTimeout = TimeSpan.FromMilliseconds(100) });

        var result = await ProcessAsync(Stalling());

        Assert.Equal(new ProcessingResult.Rejected(ProcessingRejections.TimedOut), result);
    }

    [Fact]
    public async Task A_caller_that_stops_waiting_mid_job_gets_a_cancellation_and_the_file_is_still_deleted()
    {
        using var stop = new CancellationTokenSource();
        var attachment = Stalling();
        var waiting = ProcessAsync(attachment, ct: stop.Token);
        await _stalling.WaitUntilStartedAsync();

        await stop.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        await Eventually.TrueAsync(() => !File.Exists(attachment.File.Path), "A cancelled job left its temp file behind.");
    }

    [Fact]
    public async Task A_job_cancelled_while_still_queued_is_never_converted()
    {
        var running = ProcessAsync(Stalling());
        await _stalling.WaitUntilStartedAsync();
        using var stop = new CancellationTokenSource();
        var queued = Stalling();
        var waiting = ProcessAsync(queued, ct: stop.Token);

        await stop.CancelAsync();
        _stalling.Release();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        await running;
        await Eventually.TrueAsync(() => !File.Exists(queued.File.Path), "A job cancelled in the queue left its temp file behind.");
        Assert.Equal(1, _stalling.StartedCount);
    }

    [Fact]
    public async Task One_owner_never_has_more_jobs_running_than_the_limit()
    {
        await RestartAsync(new KnowledgeFileProcessingOptions { Workers = 2, MaxActiveJobsPerUser = 1 });
        var first = ProcessAsync(Stalling());
        await _stalling.WaitUntilStartedAsync();

        var second = ProcessAsync(Stalling());
        await Task.Delay(100);

        Assert.Equal(1, _stalling.StartedCount);
        _stalling.Release();
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task Another_owners_job_runs_alongside_one_at_the_limit()
    {
        await RestartAsync(new KnowledgeFileProcessingOptions { Workers = 2, MaxActiveJobsPerUser = 1 });
        var first = ProcessAsync(Stalling());
        await _stalling.WaitUntilStartedAsync();

        var second = ProcessAsync(Stalling(), owner: OtherOwner);
        await _stalling.WaitUntilStartedAsync();

        Assert.Equal(2, _stalling.StartedCount);
        _stalling.Release();
        await Task.WhenAll(first, second);
    }

    // Waiting, not refusing: a full queue must not lose the file.
    [Fact]
    public async Task A_caller_waits_while_the_queue_is_full_and_goes_through_once_it_drains()
    {
        await RestartAsync(new KnowledgeFileProcessingOptions { Workers = 1, QueueCapacity = 1 });
        var running = ProcessAsync(Stalling());
        await _stalling.WaitUntilStartedAsync();
        var queued = ProcessAsync(Stalling(), owner: OtherOwner);

        var waitingForRoom = ProcessAsync(Markdown(), owner: "third");
        await Task.Delay(100);

        Assert.False(waitingForRoom.IsCompleted);
        _stalling.Release();
        Assert.IsType<ProcessingResult.Processed>(await waitingForRoom);
        await Task.WhenAll(running, queued);
    }

    // Without the drain, the queued caller would wait forever.
    [Fact]
    public async Task Stopping_the_service_answers_every_waiting_caller_and_deletes_their_files()
    {
        var running = Stalling();
        var runningResult = ProcessAsync(running);
        await _stalling.WaitUntilStartedAsync();
        var queued = Stalling();
        var queuedResult = ProcessAsync(queued, owner: OtherOwner);

        await _worker.StopAsync(CancellationToken.None);

        var shuttingDown = new ProcessingResult.Rejected(ProcessingRejections.ShuttingDown);
        Assert.Equal([shuttingDown, shuttingDown], await Task.WhenAll(runningResult, queuedResult));
        Assert.False(File.Exists(running.File.Path) || File.Exists(queued.File.Path));
    }

    [Fact]
    public async Task An_attachment_handed_in_after_the_service_stopped_is_refused_and_deleted()
    {
        await _worker.StopAsync(CancellationToken.None);
        var late = Markdown();

        var result = await ProcessAsync(late);

        Assert.Equal(new ProcessingResult.Rejected(ProcessingRejections.ShuttingDown), result);
        Assert.False(File.Exists(late.File.Path));
    }
}
