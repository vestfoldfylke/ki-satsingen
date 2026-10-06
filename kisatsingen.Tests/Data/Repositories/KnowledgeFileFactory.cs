using System.Security.Cryptography;
using System.Text;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using kisatsingen.Services.KnowledgeFiles.Processing;

namespace kisatsingen.Tests.Data.Repositories;

internal static class KnowledgeFileFactory
{
    // The hash is the Markdown's, so two drafts with different text are never
    // duplicates of each other unless a test says so.
    public static KnowledgeFileDraft Draft(string fileName, string markdown = "# Innledning\n\nTekst.\n") =>
        new(
            FileName: fileName,
            ContentType: "text/markdown",
            SizeBytes: 1024,
            Sha256: Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(markdown))),
            Markdown: markdown,
            Origin: ContentOrigin.TextFile,
            PageCount: null,
            Summary: $"Utdrag fra {fileName}.");
}
