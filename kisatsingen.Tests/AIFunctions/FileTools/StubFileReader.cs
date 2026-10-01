using kisatsingen.Data.Entities;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using kisatsingen.Services.KnowledgeFiles.Documents;
using kisatsingen.Services.KnowledgeFiles.Reading;

namespace kisatsingen.Tests.AIFunctions.FileTools;

// One file, readable only through the owner and chat it was given, so the
// contract tests need no database.
internal sealed class StubFileReader(string ownerId, Guid chatId, Guid fileId, string markdown, ContentOrigin? origin = ContentOrigin.TextFile) : IKnowledgeFileReader
{
    private static readonly DateTimeOffset SavedAt = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private KnowledgeFileMetadata Metadata => new(
        fileId,
        "notat.md",
        "text/markdown",
        SizeBytes: 100,
        Sha256: new string('a', 64),
        Summary: "Utdrag.",
        LineCount: DocumentLines.Count(markdown),
        EstimatedTokenCount: 1,
        origin,
        PageCount: null,
        Version: 1,
        SavedAt,
        SavedAt);

    public Task<IReadOnlyList<KnowledgeFileMetadata>> ListChatFilesAsync(string owner, Guid chat, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<KnowledgeFileMetadata>>(IsScope(owner, chat) ? [Metadata] : []);

    public Task<KnowledgeFileDocument?> OpenChatFileAsync(string owner, Guid chat, Guid file, CancellationToken ct) =>
        Task.FromResult(IsScope(owner, chat) && file == fileId
            ? new KnowledgeFileDocument(Metadata, DocumentLines.Split(markdown), DocumentOutline.Build(markdown))
            : null);

    private bool IsScope(string owner, Guid chat) => owner == ownerId && chat == chatId;
}
