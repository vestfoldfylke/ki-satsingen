namespace kisatsingen.Data.Entities;

// One contiguous span of a knowledge file's extracted text.
//
// Chunks partition the document — they never overlap. That is a hard
// requirement, not a tuning choice: the uploaded bytes are discarded, so
// concatenating chunks in Sequence order is the only way back to the document,
// and overlap would double-count every seam. Nothing is lost by it, because
// retrieval works by asking for a range rather than by similarity, and a model
// that needs more context asks for the neighbouring chunk.
public sealed class KnowledgeFileChunk
{
    public Guid Id { get; init; }
    public Guid KnowledgeFileId { get; init; }

    // Zero-based, dense, unique within the file. Assigned in code from the order
    // the chunker produced, not from a database sequence — unlike ChatMessage.Seq
    // there is no second table to interleave with and no concurrent writer, since
    // every chunk of a file is written in one batch by the request that uploaded
    // it. The unique index rules out two chunks in one position; density is the
    // repository's doing, and survives only while this stays the single write
    // path.
    public required int Sequence { get; init; }

    // Where this chunk sits in the document's structure — a heading, or a path of
    // them. Null for text that appears before any heading.
    public string? Heading { get; init; }

    public required string Content { get; init; }

    // The chunker's own estimate, not a count from the serving model's tokenizer.
    // Good enough to budget a context window against; not exact, and named so no
    // caller treats it as billing truth.
    public required int EstimatedTokenCount { get; init; }

    public KnowledgeFile? KnowledgeFile { get; init; }
}
