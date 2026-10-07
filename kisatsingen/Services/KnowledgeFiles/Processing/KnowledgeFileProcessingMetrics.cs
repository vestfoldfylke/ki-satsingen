using kisatsingen.Constants;
using Vestfold.Extensions.Metrics.Services;
using MetricTimer = Prometheus.ITimer;

namespace kisatsingen.Services.KnowledgeFiles.Processing;

// Every metric processing records, in one place and never throwing: Prometheus
// throws on a registration or label conflict, and that must never replace a
// job's own outcome or leave its caller waiting. A metric that fails is logged
// and skipped.
public sealed class KnowledgeFileProcessingMetrics(IMetricsService metrics, ILogger<KnowledgeFileProcessingMetrics> logger)
{
    private static readonly string Prefix = $"{MetricConstants.MetricsAppPrefix}_KnowledgeFile";

    // Started when a job is queued, observed when a worker takes it.
    public MetricTimer? StartQueueWait() =>
        StartTimer($"{Prefix}_QueueWait", "Time a knowledge file waited for a worker");

    public MetricTimer? StartConversion() =>
        StartTimer($"{Prefix}_Duration", "Time spent converting a knowledge file");

    public void Observe(MetricTimer? timer)
    {
        try
        {
            timer?.ObserveDuration();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not record a knowledge-file processing duration.");
        }
    }

    public void CountOutcome(string contentType, string outcome)
    {
        try
        {
            metrics.Count(
                $"{Prefix}_Processed",
                "Knowledge files processed, by content type and outcome",
                (MetricConstants.MetricsContentTypeLabelName, contentType),
                (MetricConstants.MetricsResultLabelName, outcome));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not count the {Outcome} outcome for a {ContentType} knowledge file.", outcome, contentType);
        }
    }

    private MetricTimer? StartTimer(string name, string description)
    {
        try
        {
            return metrics.Histogram(name, description);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not start the {Metric} timer.", name);
            return null;
        }
    }
}
