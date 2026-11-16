using kisatsingen.Services.KnowledgeFiles.Conversion;

namespace kisatsingen.Services.KnowledgeFiles.Pipeline;

// Failures the user can act on. A bug in a converter is an exception, not one
// of these.
//
// Every result produced after a conversion carries what that conversion spent,
// so the caller can record it whatever became of the file. No default, so each
// result the pipeline creates states its usage.
public abstract record KnowledgeFilePipelineResult(ConversionTokenUsage? ConversionTokenUsage)
{
    public sealed record Completed(NewKnowledgeFile File, ConversionTokenUsage? ConversionTokenUsage)
        : KnowledgeFilePipelineResult(ConversionTokenUsage);

    // Shown to the user as is.
    public sealed record Rejected(string Reason, ConversionTokenUsage? ConversionTokenUsage)
        : KnowledgeFilePipelineResult(ConversionTokenUsage);
}
