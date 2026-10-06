namespace kisatsingen.Services.KnowledgeFiles.Processing;

// For progress in the UI only; never stored.
public enum KnowledgeFileProcessingStage
{
    Queued,
    Converting,
    Summarizing
}
