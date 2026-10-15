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

    public void Validate()
    {
        if (MaxConcurrentConversions <= 0 || ConversionTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"Every '{SectionName}' setting must be positive. Check the '{SectionName}' section in configuration.");
        }
    }
}
