using kisatsingen.Data.Entities;
using kisatsingen.Services.KnowledgeFiles.Pipeline;

namespace kisatsingen.Data.Repositories;

// A file is reached only through its chat or assistant: every method checks
// the user against that parent, never against the file itself. When the user
// may not reach the parent, reads and deletes return nothing (null, empty,
// false), so a file id alone never reaches another scope.
public interface IKnowledgeFileRepository
{
    // One method per scope rather than one taking a nullable pair, so a caller
    // cannot express "both parents" or "neither" and the check constraint is a
    // backstop rather than the only guard. Both throw when the user may not
    // use the scope.
    //
    // The assistant methods check only that the user may use the assistant;
    // adding and deleting its files also needs CanEditAssistant, authorized
    // before they are called. A file is rejected once the assistant has
    // Assistant.MaxKnowledgeFiles.
    Task<KnowledgeFileSaveResult> CreateFileForAssistantAsync(Guid userId, Guid assistantId, NewKnowledgeFile newFile, CancellationToken ct = default);
    Task<KnowledgeFileSaveResult> CreateFileForChatAsync(Guid userId, Guid chatId, NewKnowledgeFile newFile, CancellationToken ct = default);

    Task<KnowledgeFileText?> GetChatFileTextAsync(Guid userId, Guid chatId, Guid knowledgeFileId, CancellationToken ct = default);

    Task<KnowledgeFileMetadata?> FindChatFileBySha256Async(Guid userId, Guid chatId, string sha256, CancellationToken ct = default);

    Task<IReadOnlyList<KnowledgeFileMetadata>> ListFilesForAssistantAsync(Guid userId, Guid assistantId, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeFileMetadata>> ListFilesForChatAsync(Guid userId, Guid chatId, CancellationToken ct = default);

    Task<bool> DeleteAssistantFileAsync(Guid userId, Guid assistantId, Guid knowledgeFileId, CancellationToken ct = default);
}
