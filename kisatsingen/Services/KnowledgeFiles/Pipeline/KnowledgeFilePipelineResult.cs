namespace kisatsingen.Services.KnowledgeFiles.Pipeline;

// Failures the user can act on. A bug in a converter is an exception, not one
// of these.
public abstract record KnowledgeFilePipelineResult
{
    public sealed record Completed(NewKnowledgeFile File) : KnowledgeFilePipelineResult;

    // Shown to the user as is.
    public sealed record Rejected(string Reason) : KnowledgeFilePipelineResult;
}
