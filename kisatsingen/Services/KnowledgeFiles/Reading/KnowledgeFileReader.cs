using kisatsingen.Data.Repositories;
using kisatsingen.Data.Entities;
using kisatsingen.Services.KnowledgeFiles.Documents;

namespace kisatsingen.Services.KnowledgeFiles.Reading;

// No cache: splitting and outlining a file takes milliseconds at the sizes the
// token cap allows.
public sealed class KnowledgeFileReader(IKnowledgeFileRepository repository) : IKnowledgeFileReader
{
    public Task<IReadOnlyList<KnowledgeFileMetadata>> ListChatFilesAsync(KnowledgeFileScope scope, CancellationToken ct)
        => repository.ListFilesForChatAsync(scope.UserId, scope.ChatId, ct);

    public async Task<IReadOnlyList<KnowledgeFileMetadata>> ListAssistantFilesAsync(KnowledgeFileScope scope, CancellationToken ct)
        => scope.AssistantId is Guid assistantId
            ? await repository.ListFilesForAssistantAsync(scope.UserId, assistantId, ct)
            : [];

    // The id doesn't say whether the file is the chat's or the assistant's, so
    // try the assistant first, when the chat has one. The extra lookup is
    // cheap, and simpler than keeping track of which ids belong where.
    public async Task<KnowledgeFileDocument?> OpenFileAsync(KnowledgeFileScope scope, Guid fileId, CancellationToken ct)
    {
        var text = scope.AssistantId is Guid assistantId
            ? await repository.GetAssistantFileTextAsync(scope.UserId, assistantId, fileId, ct)
            : null;
        text ??= await repository.GetChatFileTextAsync(scope.UserId, scope.ChatId, fileId, ct);

        return text is null
            ? null
            : new KnowledgeFileDocument(text.File, DocumentLines.Split(text.Markdown), DocumentOutline.Build(text.Markdown));
    }
}
