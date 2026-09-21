namespace kisatsingen.Data.Entities;

// Processing output on its way to the repository. Not a KnowledgeFile: the
// scope columns and the derived totals are the repository's to assign, and a
// caller that could set them could make the totals disagree with the chunks.
public sealed record KnowledgeFileDraft(
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    string Summary,
    string? TableOfContents,
    int? PageCount,
    string? Language,
    IReadOnlyList<KnowledgeFileChunkDraft> Chunks);
