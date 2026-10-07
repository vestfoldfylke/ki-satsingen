namespace kisatsingen.Services.KnowledgeFiles.Processing;

// Defaults in code, overridable per environment under this section name.
public sealed class KnowledgeFileProcessingOptions
{
    public const string SectionName = "KnowledgeFileProcessing";

    // Jobs hold a temp-file path, not bytes, so a deep queue is cheap.
    public int QueueCapacity { get; init; } = 50;

    // Text conversion needs little CPU. Raise it once summaries are model
    // calls, which wait on the network rather than the processor.
    public int Workers { get; init; } = 2;

    // Keeps one user, or later an assistant batch, from filling the queue.
    public int MaxActiveJobsPerUser { get; init; } = 3;

    // A stuck converter must not hold a turn open forever.
    public TimeSpan JobTimeout { get; init; } = TimeSpan.FromMinutes(2);

    // Far above any screenshot; checked from the header before decoding.
    public long MaxImageSidePixels { get; init; } = 8_000;

    // 40 MP at 4 bytes per pixel is about 160 MB to decode.
    public long MaxImagePixels { get; init; } = 40_000_000;

    public void Validate()
    {
        if (QueueCapacity <= 0 || Workers <= 0 || MaxActiveJobsPerUser <= 0 || JobTimeout <= TimeSpan.Zero
            || MaxImageSidePixels <= 0 || MaxImagePixels <= 0)
        {
            throw new InvalidOperationException(
                $"Every '{SectionName}' setting must be positive. Check the '{SectionName}' section in configuration.");
        }
    }
}
