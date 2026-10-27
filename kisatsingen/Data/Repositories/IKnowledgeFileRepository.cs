using kisatsingen.Data.Entities;
using kisatsingen.Services.KnowledgeFiles.Pipeline;

namespace kisatsingen.Data.Repositories;

public interface IKnowledgeFileRepository
{
    // One method per scope rather than one taking a nullable pair, so a caller
    // cannot express "both parents" or "neither" and the check constraint is a
    // backstop rather than the only guard. Both throw when the scope is not
    // the owner's.
    Task<KnowledgeFileSaveResult> CreateFileForAssistantAsync(Guid ownerId, Guid assistantId, NewKnowledgeFile newFile, CancellationToken ct = default);
    Task<KnowledgeFileSaveResult> CreateFileForChatAsync(Guid ownerId, Guid chatId, NewKnowledgeFile newFile, CancellationToken ct = default);

    Task<KnowledgeFileMetadata?> GetFileAsync(Guid ownerId, Guid knowledgeFileId, CancellationToken ct = default);

    // The only read that loads Markdown. Null unless the file is the owner's
    // and in that chat, so a file id alone never reaches another scope.
    Task<KnowledgeFileText?> GetChatFileTextAsync(Guid ownerId, Guid chatId, Guid knowledgeFileId, CancellationToken ct = default);

    // The owner's file in that chat with this content, if any; the unique index
    // allows at most one.
    Task<KnowledgeFileMetadata?> FindChatFileBySha256Async(Guid ownerId, Guid chatId, string sha256, CancellationToken ct = default);

    Task<IReadOnlyList<KnowledgeFileMetadata>> ListFilesForAssistantAsync(Guid ownerId, Guid assistantId, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeFileMetadata>> ListFilesForChatAsync(Guid ownerId, Guid chatId, CancellationToken ct = default);

    Task<bool> DeleteFileAsync(Guid ownerId, Guid knowledgeFileId, CancellationToken ct = default);
}
