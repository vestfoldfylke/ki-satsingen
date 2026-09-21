using kisatsingen.Data.Entities;

namespace kisatsingen.Data.Repositories;

public interface IKnowledgeFileRepository
{
    // One method per scope rather than one taking a nullable pair, so a caller
    // cannot express "both parents" or "neither" and the check constraint is a
    // backstop rather than the only guard.
    Task<KnowledgeFile> CreateFileForAssistantAsync(string ownerId, Guid assistantId, KnowledgeFileDraft draft, CancellationToken ct = default);
    Task<KnowledgeFile> CreateFileForChatAsync(string ownerId, Guid chatId, KnowledgeFileDraft draft, CancellationToken ct = default);

    // Metadata only — Summary and TableOfContents included, chunks never.
    Task<KnowledgeFile?> GetFileAsync(string ownerId, Guid knowledgeFileId, CancellationToken ct = default);

    Task<IReadOnlyList<KnowledgeFileSummary>> ListFilesForAssistantAsync(string ownerId, Guid assistantId, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeFileSummary>> ListFilesForChatAsync(string ownerId, Guid chatId, CancellationToken ct = default);

    // Inclusive on both ends, in document order, capped at MaxChunkSpan.
    Task<IReadOnlyList<KnowledgeFileChunk>> GetChunksAsync(string ownerId, Guid knowledgeFileId, int firstSequence, int lastSequence, CancellationToken ct = default);

    Task<bool> DeleteFileAsync(string ownerId, Guid knowledgeFileId, CancellationToken ct = default);
}
