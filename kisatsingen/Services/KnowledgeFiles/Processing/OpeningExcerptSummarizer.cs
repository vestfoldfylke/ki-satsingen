using kisatsingen.Services.KnowledgeFiles.Documents;

namespace kisatsingen.Services.KnowledgeFiles.Processing;

// Stand-in until summaries come from a model: the opening of the document,
// labelled as an excerpt so no one mistakes it for a summary.
public sealed class OpeningExcerptSummarizer : IDocumentSummarizer
{
    private const int ExcerptLength = 600;
    private const string ExcerptPrefix = "Utdrag fra starten av dokumentet: ";

    public Task<string?> SummarizeAsync(string markdown, IReadOnlyList<OutlineEntry> outline, CancellationToken ct)
    {
        var opening = markdown.Length <= ExcerptLength ? markdown : markdown[..ExcerptLength];
        var excerpt = string.Join(' ', opening.Split((char[])['\n', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries));

        return Task.FromResult<string?>(excerpt.Length == 0
            ? null
            : ExcerptPrefix + excerpt + (markdown.Length > ExcerptLength ? "…" : string.Empty));
    }
}
