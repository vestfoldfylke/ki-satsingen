namespace kisatsingen.Services.Attachments;

// The last cleanup path: attachments nobody sent or removed, and temp files
// nothing tracks (a crash between creating a file and registering it).
public sealed class TempFileSweeper(
    PendingAttachmentRegistry registry,
    TempFileStore store,
    AttachmentOptions options,
    TimeProvider time,
    ILogger<TempFileSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.TempFileSweepInterval, time);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                Sweep();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    // Registry first, so the files it tracked are gone before the store looks
    // for files nothing tracks.
    internal void Sweep()
    {
        try
        {
            var cutoff = time.GetUtcNow() - options.TempFileMaxAge;
            var abandoned = registry.RemoveOlderThan(cutoff);
            var orphaned = store.DeleteOlderThan(cutoff);

            if (abandoned > 0 || orphaned > 0)
            {
                logger.LogInformation("Swept {Abandoned} abandoned attachments and {Orphaned} untracked temp files older than {MaxAge}.", abandoned, orphaned, options.TempFileMaxAge);
            }
        }
        // One failed sweep must not end every later one.
        catch (Exception ex)
        {
            logger.LogError(ex, "The attachment temp-file sweep failed. It runs again in {Interval}.", options.TempFileSweepInterval);
        }
    }
}
