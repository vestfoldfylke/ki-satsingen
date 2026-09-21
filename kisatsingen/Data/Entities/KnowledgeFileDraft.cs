using System.Collections.Generic;

namespace kisatsingen.Data.Entities;

// The finished output of processing, handed to the repository to persist in one
// transaction. It is deliberately not a KnowledgeFile: ChunkCount,
// EstimatedTokenCount, Sequence and the scope columns are all assigned by the
// repository, and a caller that could set them could make the stored totals
// disagree with the stored chunks.
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
