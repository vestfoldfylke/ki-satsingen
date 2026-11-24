namespace kisatsingen.Services.WordDocuments;

// Title becomes the file's document title (an accessibility requirement) and
// its file name; in a report it is also the cover title. Subtitle, Date and
// CoverText are the report cover's.
public sealed record WordDocumentSpec(
    string Title,
    WordDocumentTemplate Template,
    IReadOnlyList<DocumentBlockSpec> Blocks,
    string? Subtitle = null,
    string? Date = null,
    string? CoverText = null);
