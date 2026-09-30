using kisatsingen.Data.Entities;
using kisatsingen.Services.KnowledgeFiles.Processing;

namespace kisatsingen.Data.Repositories;

public interface IKnowledgeFileRepository
{
    // One method per scope rather than one taking a nullable pair, so a caller
    // cannot express "both parents" or "neither" and the check constraint is a
    // backstop rather than the only guard. Both throw when the scope is not
    // the owner's.
    Task<KnowledgeFileSaveResult> CreateFileForAssistantAsync(string ownerId, Guid assistantId, KnowledgeFileDraft draft, CancellationToken ct = default);
    Task<KnowledgeFileSaveResult> CreateFileForChatAsync(string ownerId, Guid chatId, KnowledgeFileDraft draft, CancellationToken ct = default);

    Task<KnowledgeFileMetadata?> GetFileAsync(string ownerId, Guid knowledgeFileId, CancellationToken ct = default);

    Task<IReadOnlyList<KnowledgeFileMetadata>> ListFilesForAssistantAsync(string ownerId, Guid assistantId, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeFileMetadata>> ListFilesForChatAsync(string ownerId, Guid chatId, CancellationToken ct = default);

    Task<bool> DeleteFileAsync(string ownerId, Guid knowledgeFileId, CancellationToken ct = default);
}
