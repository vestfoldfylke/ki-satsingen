namespace kisatsingen.Services.Attachments;

// The last cleanup path for attachments nobody sent or removed. Temp files
// nothing tracks are left to the next startup, which wipes the folder.
public sealed class AttachmentSweeper(
    PendingAttachmentRegistry registry,
    AttachmentOptions options,
    TimeProvider time,
    ILogger<AttachmentSweeper> logger) : BackgroundService
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

    internal void Sweep()
    {
        try
        {
            var cutoff = time.GetUtcNow() - options.TempFileMaxAge;
            var abandoned = registry.RemoveOlderThan(cutoff);

            if (abandoned > 0)
            {
                logger.LogInformation("Swept {Abandoned} abandoned attachments older than {MaxAge}.", abandoned, options.TempFileMaxAge);
            }
        }
        // One failed sweep must not end every later one.
        catch (Exception ex)
        {
            logger.LogError(ex, "The attachment temp-file sweep failed. It runs again in {Interval}.", options.TempFileSweepInterval);
        }
    }
}
