namespace kisatsingen.Data.Entities;

// A document that has been uploaded, extracted and chunked. The uploaded bytes
// are not kept anywhere — the chunks are the only copy of the content, which is
// why this row is written once, complete, at the end of processing rather than
// created empty and filled in. If the row exists, the file is usable; there is
// no half-processed state to guard against, and every field below that could
// only be absent mid-processing is non-nullable to say so.
//
// The consequence to know before changing the chunker: a file can never be
// re-chunked. There is nothing to re-read.
public sealed class KnowledgeFile
{
    public Guid Id { get; init; }

    // Denormalised from the owning assistant or chat so repository queries can
    // filter by owner without a join, matching the defence-in-depth owner filter
    // every repository method takes. Safe only while nothing transfers ownership
    // of an assistant or a chat — add a transfer feature and this has to move
    // with it.
    public required string OwnerId { get; init; }

    // Exactly one of these is set, enforced by a check constraint in
    // AppDbContext. The repository offers a separate create method per scope so
    // the invalid combinations cannot be expressed by a caller in the first
    // place; the constraint is the backstop, not the only guard.
    //
    // Neither has a navigation to its parent. Loading a file's assistant or chat
    // is not something any query does, and an unused navigation is a loading
    // path that exists only to be taken by mistake.
    public Guid? AssistantId { get; init; }
    public Guid? ChatId { get; init; }

    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }

    // Of the uploaded bytes, which are then discarded. Recorded, not yet used:
    // with the bytes gone it can no longer answer "has this been reprocessed",
    // only "is this byte-for-byte a file already attached here". Acting on that
    // needs a duplicate policy and a unique index on (scope, Sha256); neither
    // exists, so today this is evidence kept for when one is decided.
    public required string Sha256 { get; init; }

    public required string Summary { get; init; }

    // Null when the document genuinely has no headings to build one from, which
    // is a permanent property of the file rather than a gap in processing.
    public string? TableOfContents { get; init; }

    // Derived from Chunks and written in the same transaction, so a file list can
    // show size and cost without aggregating the chunk table. The repository
    // computes both from the chunks it is handed — a caller cannot supply
    // figures that disagree with the rows.
    public required int ChunkCount { get; init; }
    public required int EstimatedTokenCount { get; init; }

    public int? PageCount { get; init; }
    public string? Language { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public List<KnowledgeFileChunk> Chunks { get; init; } = [];
}
