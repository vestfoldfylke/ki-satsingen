using kisatsingen.Data.Entities;

namespace kisatsingen.Tests.Data.Repositories;

internal static class KnowledgeFileFactory
{
    public static KnowledgeFileDraft Draft(string fileName, params string[] chunkContents) =>
        new(
            FileName: fileName,
            ContentType: "application/pdf",
            SizeBytes: 1024,
            Sha256: new string('a', 64),
            Summary: $"A summary of {fileName}.",
            TableOfContents: "1. Innledning",
            PageCount: 3,
            Language: "nb",
            Chunks: [.. chunkContents.Select((content, index) =>
                new KnowledgeFileChunkDraft($"Heading {index}", content, EstimatedTokenCount: content.Length))]);
}
