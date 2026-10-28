using kisatsingen.Data.Entities;
using kisatsingen.Services.KnowledgeFiles.Pipeline;

namespace kisatsingen.Data.Repositories;

public interface IKnowledgeFileRepository
{
    // One method per scope rather than one taking a nullable pair, so a caller
    // cannot express "both parents" or "neither" and the check constraint is a
    // backstop rather than the only guard. Both throw when the user may not
    // use the scope.
    //
    // The assistant methods check only that the user may use the assistant;
    // adding and deleting its files also needs CanEditAssistant, authorized
    // before they are called. A file added to an assistant is its owner's,
    // whoever adds it, and is rejected once the assistant has
    // Assistant.MaxKnowledgeFiles.
    Task<KnowledgeFileSaveResult> CreateFileForAssistantAsync(Guid userId, Guid assistantId, NewKnowledgeFile newFile, CancellationToken ct = default);
    Task<KnowledgeFileSaveResult> CreateFileForChatAsync(Guid userId, Guid chatId, NewKnowledgeFile newFile, CancellationToken ct = default);

    Task<KnowledgeFileMetadata?> GetFileAsync(Guid ownerId, Guid knowledgeFileId, CancellationToken ct = default);

    // The only read that loads Markdown. Null unless the file is the owner's
    // and in that chat, so a file id alone never reaches another scope.
    Task<KnowledgeFileText?> GetChatFileTextAsync(Guid ownerId, Guid chatId, Guid knowledgeFileId, CancellationToken ct = default);

    // The owner's file in that chat with this content, if any; the unique index
    // allows at most one.
    Task<KnowledgeFileMetadata?> FindChatFileBySha256Async(Guid ownerId, Guid chatId, string sha256, CancellationToken ct = default);

    // Empty when the user may not use the assistant.
    Task<IReadOnlyList<KnowledgeFileMetadata>> ListFilesForAssistantAsync(Guid userId, Guid assistantId, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeFileMetadata>> ListFilesForChatAsync(Guid ownerId, Guid chatId, CancellationToken ct = default);

    // False when the file is not in that assistant or the user may not use it.
    Task<bool> DeleteAssistantFileAsync(Guid userId, Guid assistantId, Guid knowledgeFileId, CancellationToken ct = default);
    Task<bool> DeleteFileAsync(Guid ownerId, Guid knowledgeFileId, CancellationToken ct = default);
}
