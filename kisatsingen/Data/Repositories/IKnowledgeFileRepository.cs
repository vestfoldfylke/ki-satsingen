using kisatsingen.Data.Entities;
using kisatsingen.Services.KnowledgeFiles.Pipeline;

namespace kisatsingen.Data.Repositories;

// Every method checks the user against the file's chat or assistant, never
// the file itself. Reads and deletes return nothing when the user may not
// reach that parent; creates throw.
//
// For an assistant, the check is only that the user may use it. Adding or
// deleting its files also needs CanEditAssistant, authorized by the caller.
public interface IKnowledgeFileRepository
{
    // One method per scope, so a caller cannot pass both chatId and assistantId or neither.
    Task<KnowledgeFileSaveResult> CreateFileForAssistantAsync(Guid userId, Guid assistantId, NewKnowledgeFile newFile, CancellationToken ct = default);
    Task<KnowledgeFileSaveResult> CreateFileForChatAsync(Guid userId, Guid chatId, NewKnowledgeFile newFile, CancellationToken ct = default);

    Task<KnowledgeFileText?> GetChatFileTextAsync(Guid userId, Guid chatId, Guid knowledgeFileId, CancellationToken ct = default);

    Task<KnowledgeFileMetadata?> FindChatFileBySha256Async(Guid userId, Guid chatId, string sha256, CancellationToken ct = default);

    Task<IReadOnlyList<KnowledgeFileMetadata>> ListFilesForAssistantAsync(Guid userId, Guid assistantId, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeFileMetadata>> ListFilesForChatAsync(Guid userId, Guid chatId, CancellationToken ct = default);

    Task<bool> DeleteAssistantFileAsync(Guid userId, Guid assistantId, Guid knowledgeFileId, CancellationToken ct = default);
}
