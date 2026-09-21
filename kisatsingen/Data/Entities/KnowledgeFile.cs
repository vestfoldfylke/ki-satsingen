namespace kisatsingen.Data.Entities;

// The uploaded bytes are discarded after processing, so the chunks are the only
// copy of the content and a file can never be re-chunked. The row is written
// once, complete, at the end of processing — if it exists, the file is usable,
// which is why the fields that could only be absent mid-processing are not
// nullable.
public sealed class KnowledgeFile
{
    public Guid Id { get; init; }

    // Denormalised from the owning assistant or chat so repository queries can
    // filter by owner without a join. Safe only while nothing transfers
    // ownership of an assistant or a chat.
    public required string OwnerId { get; init; }

    // Exactly one is set — see the check constraint in AppDbContext.
    public Guid? AssistantId { get; init; }
    public Guid? ChatId { get; init; }

    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }

    // Recorded, not yet used: with the bytes gone it can only answer "is this
    // byte-for-byte a file already attached here". Acting on that needs a
    // duplicate policy and a unique index on (scope, Sha256); neither exists.
    public required string Sha256 { get; init; }

    public required string Summary { get; init; }

    // Null when the document has no headings to build one from — a property of
    // the file, not a gap in processing.
    public string? TableOfContents { get; init; }

    // Derived from Chunks in the same transaction, so a file list needs no
    // aggregate over the chunk table.
    public required int ChunkCount { get; init; }
    public required int EstimatedTokenCount { get; init; }

    public int? PageCount { get; init; }
    public string? Language { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public List<KnowledgeFileChunk> Chunks { get; init; } = [];
}
