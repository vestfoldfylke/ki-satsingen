namespace kisatsingen.Services.KnowledgeFiles.Pipeline;

// Defaults in code, overridable per environment under this section name.
public sealed class KnowledgeFilePipelineOptions
{
    public const string SectionName = "KnowledgeFilePipeline";

    // Converting is CPU work: more conversions at once than cores only take
    // turns on them, and each can hold a few hundred MB while it runs.
    public int MaxConcurrentConversions { get; init; } = Environment.ProcessorCount;

    // A stuck converter must not hold a turn open forever.
    public TimeSpan ConversionTimeout { get; init; } = TimeSpan.FromMinutes(2);

    // Far above any screenshot; checked from the header before decoding.
    public long MaxImageSidePixels { get; init; } = 8_000;

    // 40 MP at 4 bytes per pixel is about 160 MB to decode.
    public long MaxImagePixels { get; init; } = 40_000_000;

    public void Validate()
    {
        if (MaxConcurrentConversions <= 0 || ConversionTimeout <= TimeSpan.Zero
            || MaxImageSidePixels <= 0 || MaxImagePixels <= 0)
        {
            throw new InvalidOperationException(
                $"Every '{SectionName}' setting must be positive. Check the '{SectionName}' section in configuration.");
        }
    }
}
