namespace kisatsingen.Data.Entities;

// The uploaded bytes are discarded after processing, so Markdown is the only
// copy of the content. The row is written once, complete, by the save path —
// if it exists, the file is usable, so reads need no status filter.
//
// Never loaded whole to list files: Markdown is the file's entire text, so
// metadata reads go through the KnowledgeFileMetadata projection.
public sealed class KnowledgeFile
{
    public const int MaxFileNameLength = 260;
    public const int MaxContentTypeLength = 128;
    public const int MaxSummaryLength = 4_000;
    public const int MaxContentOriginLength = 64;

    // SHA-256 is exactly 32 bytes → 64 hex chars. Not a max; the check is
    // equality both ways.
    public const int Sha256HexLength = 64;

    public const int FirstVersion = 1;

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

    // Unique per scope (see AppDbContext): the backstop for refusing a file
    // that is already attached.
    public required string Sha256 { get; init; }

    // Null when the summarizer produced none; a file is usable without one.
    public string? Summary { get; init; }

    // Line endings are always '\n': the save path normalises them, and every
    // line number the tools hand out depends on it.
    public required string Markdown { get; init; }

    // Derived from Markdown by the save path, and stored only because listing
    // files needs them without loading the text.
    public required int LineCount { get; init; }
    public required int EstimatedTokenCount { get; init; }

    // A ContentOrigin name, kept as a string so a value this build does not
    // know (after a rollback, say) degrades on read instead of failing the
    // load, and is never overwritten with a fallback.
    public required string ContentOrigin { get; init; }

    public int? PageCount { get; init; }

    // Kept from the start because it is cheap now and hard to add later; edits
    // will increment it.
    public required int Version { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
