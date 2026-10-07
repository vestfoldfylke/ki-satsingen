using kisatsingen.Data.Repositories;
using kisatsingen.Data.Entities;
using kisatsingen.Services.KnowledgeFiles.Documents;

namespace kisatsingen.Services.KnowledgeFiles.Reading;

// No cache: splitting and outlining a file takes milliseconds at the sizes the
// token cap allows.
public sealed class KnowledgeFileReader(IKnowledgeFileRepository repository) : IKnowledgeFileReader
{
    public Task<IReadOnlyList<KnowledgeFileMetadata>> ListChatFilesAsync(string ownerId, Guid chatId, CancellationToken ct)
        => repository.ListFilesForChatAsync(ownerId, chatId, ct);

    public async Task<KnowledgeFileDocument?> OpenChatFileAsync(string ownerId, Guid chatId, Guid fileId, CancellationToken ct)
    {
        var text = await repository.GetChatFileTextAsync(ownerId, chatId, fileId, ct);

        return text is null
            ? null
            : new KnowledgeFileDocument(text.File, DocumentLines.Split(text.Markdown), DocumentOutline.Build(text.Markdown));
    }
}
