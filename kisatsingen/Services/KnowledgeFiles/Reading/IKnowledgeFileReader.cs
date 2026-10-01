using kisatsingen.Data.Entities;

namespace kisatsingen.Services.KnowledgeFiles.Reading;

// Every read the file tools make goes through here.
public interface IKnowledgeFileReader
{
    Task<IReadOnlyList<KnowledgeFileMetadata>> ListChatFilesAsync(string ownerId, Guid chatId, CancellationToken ct);

    // Null when the file is not in that chat or not the owner's.
    Task<KnowledgeFileDocument?> OpenChatFileAsync(string ownerId, Guid chatId, Guid fileId, CancellationToken ct);
}
