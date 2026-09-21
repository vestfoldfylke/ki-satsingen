using kisatsingen.Data.Entities;

namespace kisatsingen.Data.Repositories;

public interface IKnowledgeFileRepository
{
    // One create method per scope rather than one taking a nullable pair. A
    // caller cannot pass both parents or neither, so the check constraint in the
    // database is a backstop against a bug rather than the only thing standing
    // between the schema and a file that belongs nowhere.
    Task<KnowledgeFile> CreateFileForAssistantAsync(string ownerId, Guid assistantId, KnowledgeFileDraft draft, CancellationToken ct = default);
    Task<KnowledgeFile> CreateFileForChatAsync(string ownerId, Guid chatId, KnowledgeFileDraft draft, CancellationToken ct = default);

    // Metadata only — Summary and TableOfContents included, chunks never.
    Task<KnowledgeFile?> GetFileAsync(string ownerId, Guid knowledgeFileId, CancellationToken ct = default);

    Task<IReadOnlyList<KnowledgeFileSummary>> ListFilesForAssistantAsync(string ownerId, Guid assistantId, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeFileSummary>> ListFilesForChatAsync(string ownerId, Guid chatId, CancellationToken ct = default);

    // Inclusive on both ends, in document order. Bounded on purpose: the whole
    // point of chunking is that a caller asks for a span rather than the file.
    Task<IReadOnlyList<KnowledgeFileChunk>> GetChunksAsync(string ownerId, Guid knowledgeFileId, int firstSequence, int lastSequence, CancellationToken ct = default);

    // Chunks cascade via the FK.
    Task<bool> DeleteFileAsync(string ownerId, Guid knowledgeFileId, CancellationToken ct = default);
}
