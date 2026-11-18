namespace kisatsingen.Services.KnowledgeFiles.Pipeline;

// Defaults in code, overridable per environment under this section name.
public sealed class KnowledgeFilePipelineOptions
{
    public const string SectionName = "KnowledgeFilePipeline";

    // Converting locally is CPU work: more conversions at once than cores only
    // take turns on them, and each can hold a few hundred MB while it runs.
    public int MaxConcurrentLocalConversions { get; init; } = Environment.ProcessorCount;

    // How many files can be sent to a remote provider at once.
    // Higher uses more memory, since each file is kept in memory until the
    // provider answers. Too high can also hit the provider's rate limit, which
    // makes conversions fail.
    public int MaxConcurrentRemoteConversions { get; init; } = 8;

    // A stuck converter must not hold a turn open forever.
    public TimeSpan ConversionTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public void Validate()
    {
        if (MaxConcurrentLocalConversions <= 0 || MaxConcurrentRemoteConversions <= 0 || ConversionTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"Every '{SectionName}' setting must be positive. Check the '{SectionName}' section in configuration.");
        }
    }
}
