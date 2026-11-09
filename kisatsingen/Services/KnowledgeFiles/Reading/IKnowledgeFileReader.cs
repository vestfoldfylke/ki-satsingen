using kisatsingen.Data.Entities;

namespace kisatsingen.Services.KnowledgeFiles.Reading;

// Every read the file tools make goes through here.
public interface IKnowledgeFileReader
{
    // Empty when the chat is not the user's.
    Task<IReadOnlyList<KnowledgeFileMetadata>> ListChatFilesAsync(KnowledgeFileScope scope, CancellationToken ct);

    // Empty when the scope has no assistant, or the user may no longer use it.
    Task<IReadOnlyList<KnowledgeFileMetadata>> ListAssistantFilesAsync(KnowledgeFileScope scope, CancellationToken ct);

    // Null when the file is outside the scope.
    Task<KnowledgeFileText?> ReadFileTextAsync(KnowledgeFileScope scope, Guid fileId, CancellationToken ct);

    // Null when the file is outside the scope.
    Task<KnowledgeFileDocument?> OpenFileAsync(KnowledgeFileScope scope, Guid fileId, CancellationToken ct);
}
