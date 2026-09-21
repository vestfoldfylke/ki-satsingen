namespace kisatsingen.Data.Entities;

// Chunks partition the document and never overlap: the uploaded bytes are gone,
// so concatenating them in Sequence order is the only way back to the document
// and overlap would double-count every seam. Retrieval asks for a range rather
// than by similarity, so nothing needs the overlap.
public sealed class KnowledgeFileChunk
{
    public Guid Id { get; init; }
    public Guid KnowledgeFileId { get; init; }

    // Zero-based and dense. Assigned in code rather than from a database
    // sequence like ChatMessage.Seq: every chunk of a file is written in one
    // batch, with no second table to interleave with and no concurrent writer.
    public required int Sequence { get; init; }

    // Where the chunk sits in the document's structure — a heading or a path of
    // them. Null for text before any heading.
    public string? Heading { get; init; }

    public required string Content { get; init; }
    public required int EstimatedTokenCount { get; init; }

    public KnowledgeFile? KnowledgeFile { get; init; }
}
