namespace kisatsingen.Services.KnowledgeFiles.Processing;

// Failures the user can act on. A bug in a converter is an exception, not one
// of these.
public abstract record KnowledgeFileProcessingResult
{
    public sealed record Processed(KnowledgeFileDraft Draft) : KnowledgeFileProcessingResult;

    // Shown to the user as is.
    public sealed record Rejected(string Reason) : KnowledgeFileProcessingResult;
}
