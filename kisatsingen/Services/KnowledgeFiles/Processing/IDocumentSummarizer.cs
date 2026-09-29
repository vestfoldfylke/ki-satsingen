using kisatsingen.Services.KnowledgeFiles.Documents;

namespace kisatsingen.Services.KnowledgeFiles.Processing;

// Given the outline and the text, but meant to read only the outline and the
// start: a summary is for choosing where to read, not a digest of all of it.
// Null when there is nothing worth saying; a file needs no summary to be used.
public interface IDocumentSummarizer
{
    Task<string?> SummarizeAsync(string markdown, IReadOnlyList<OutlineEntry> outline, CancellationToken ct);
}
