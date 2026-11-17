using kisatsingen.Constants;
using Vestfold.Extensions.Metrics.Services;
using MetricTimer = Prometheus.ITimer;

namespace kisatsingen.Services.KnowledgeFiles.Pipeline;

// Every metric the pipeline records, in one place and never throwing: a
// metric must never replace a file's own outcome. A metric that fails is
// logged and skipped.
public sealed class KnowledgeFilePipelineMetrics(IMetricsService metrics, ILogger<KnowledgeFilePipelineMetrics> logger)
{
    private static readonly string Prefix = $"{MetricConstants.MetricsAppPrefix}_knowledge_file";

    public MetricTimer? StartConversion(string pool) =>
        StartTimer($"{Prefix}_conversion_duration_seconds", "Time spent converting a knowledge file, by pool", pool);

    public MetricTimer? StartSlotWait(string pool) =>
        StartTimer($"{Prefix}_conversion_slot_wait_seconds", "Time a knowledge file waited for a free conversion slot, by pool", pool);

    public void Observe(MetricTimer? timer)
    {
        try
        {
            timer?.ObserveDuration();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not record a knowledge-file pipeline timing.");
        }
    }

    public void CountOutcome(string contentType, string outcome)
    {
        try
        {
            metrics.Count(
                $"{Prefix}_converted_total",
                "Knowledge file conversions, by content type and outcome",
                (MetricConstants.MetricsContentTypeLabelName, contentType),
                (MetricConstants.MetricsResultLabelName, outcome));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not count the {Outcome} outcome for a {ContentType} knowledge file.", outcome, contentType);
        }
    }

    private MetricTimer? StartTimer(string name, string description, string pool)
    {
        try
        {
            return metrics.Histogram(name, description, (MetricConstants.MetricsPoolLabelName, pool));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not start the {Metric} timer.", name);
            return null;
        }
    }
}
