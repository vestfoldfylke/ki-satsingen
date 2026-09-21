namespace kisatsingen.Data.Entities;

// What a file list needs, and nothing that grows with the document. Returning
// the entity instead would put Summary and TableOfContents on every row of a
// list view, and leave Chunks one forgotten Include away from loading the whole
// corpus. This projection makes that structurally impossible.
public sealed record KnowledgeFileSummary(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    int ChunkCount,
    int EstimatedTokenCount,
    DateTimeOffset CreatedAt);
