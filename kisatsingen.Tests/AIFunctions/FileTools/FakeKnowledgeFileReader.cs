using kisatsingen.Data.Entities;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using kisatsingen.Services.KnowledgeFiles.Documents;
using kisatsingen.Services.KnowledgeFiles.Reading;

namespace kisatsingen.Tests.AIFunctions.FileTools;

// One chat file, readable only through the scope it was given, so the contract
// tests need no database.
internal sealed class FakeKnowledgeFileReader(KnowledgeFileScope scope, Guid fileId, string markdown, ContentOrigin? origin = ContentOrigin.TextFile) : IKnowledgeFileReader
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

    public Task<IReadOnlyList<KnowledgeFileMetadata>> ListChatFilesAsync(KnowledgeFileScope asked, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<KnowledgeFileMetadata>>(asked == scope ? [Metadata] : []);

    public Task<IReadOnlyList<KnowledgeFileMetadata>> ListAssistantFilesAsync(KnowledgeFileScope asked, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<KnowledgeFileMetadata>>([]);

    public Task<KnowledgeFileDocument?> OpenFileAsync(KnowledgeFileScope asked, Guid file, CancellationToken ct) =>
        Task.FromResult(asked == scope && file == fileId
            ? new KnowledgeFileDocument(Metadata, DocumentLines.Split(markdown), DocumentOutline.Build(markdown))
            : null);
}
