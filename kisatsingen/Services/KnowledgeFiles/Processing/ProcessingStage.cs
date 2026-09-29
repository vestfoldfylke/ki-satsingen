namespace kisatsingen.Services.KnowledgeFiles.Processing;

// For progress in the UI only; never stored.
public enum ProcessingStage
{
    Queued,
    Converting,
    Summarizing
}
