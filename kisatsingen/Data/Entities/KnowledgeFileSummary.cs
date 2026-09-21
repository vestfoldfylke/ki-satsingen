namespace kisatsingen.Data.Entities;

// A projection rather than the entity, so a file list cannot carry Summary and
// TableOfContents on every row or load Chunks through a stray Include.
public sealed record KnowledgeFileSummary(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    int ChunkCount,
    int EstimatedTokenCount,
    DateTimeOffset CreatedAt);
