namespace kisatsingen.Data.Entities;

// No Sequence: position comes from list order, so a caller cannot leave a gap
// or repeat a number.
public sealed record KnowledgeFileChunkDraft(string? Heading, string Content, int EstimatedTokenCount);
