using kisatsingen.Constants;
using Vestfold.Extensions.Metrics.Services;
using MetricTimer = Prometheus.ITimer;

namespace kisatsingen.Services.Attachments;

// Every metric attaching and uploading record, in one place and never
// throwing: Prometheus throws on a registration or label conflict, and this
// runs in UI event handlers, where an exception would end the circuit. A
// metric that fails is logged and skipped.
public sealed class AttachmentMetrics(IMetricsService metrics, ILogger<AttachmentMetrics> logger)
{
    private static readonly string Prefix = $"{MetricConstants.MetricsAppPrefix}_attachment";

    private static class FolderStateLabel
    {
        public const string Uploading = "uploading";
        public const string Ready = "ready";
    }

    public void CountRejected(UploadRejection rejection)
    {
        try
        {
            metrics.Count(
                $"{Prefix}_rejected_total",
                "Refused files, by reason",
                (MetricConstants.MetricsReasonLabelName, rejection.Key));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not count a file refused as {RejectionKey}.", rejection.Key);
        }
    }

    public MetricTimer? StartUpload()
    {
        try
        {
            return metrics.Histogram($"{Prefix}_upload_duration_seconds", "Time spent uploading a file");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not start the upload timer.");
            return null;
        }
    }

    // For a completed upload only; the timer records nothing until this, so
    // an upload that fails or stops is left out.
    public void RecordUpload(MetricTimer? timer, long sizeBytes)
    {
        try
        {
            timer?.ObserveDuration();

            // The package counts in int: a file over 2 GiB is logged, not miscounted.
            metrics.Count($"{Prefix}_uploaded_bytes_total", "Bytes of completed uploads", checked((int)sizeBytes));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not record a completed upload of {SizeBytes} bytes.", sizeBytes);
        }
    }

    // Runs before every scrape, where a throw fails the whole scrape and not
    // just this gauge; a missing folder throws.
    public void RecordFolderUsage(AttachmentStorage storage)
    {
        try
        {
            var name = $"{Prefix}_folder_bytes";
            const string description = "Bytes in the attachment temp folder, by state";
            metrics.Gauge(name, description, storage.TotalUploadingUsage().ByteCount, (MetricConstants.MetricsStateLabelName, FolderStateLabel.Uploading));
            metrics.Gauge(name, description, storage.TotalReadyUsage().ByteCount, (MetricConstants.MetricsStateLabelName, FolderStateLabel.Ready));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not measure the attachment temp folder {Folder} for metrics.", storage.Folder);
        }
    }
}
