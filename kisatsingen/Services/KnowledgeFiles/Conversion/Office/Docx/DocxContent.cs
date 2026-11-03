using DocumentFormat.OpenXml.Wordprocessing;

namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

// Everything the writer needs, loaded and checked while the file was open.
// The trees stay usable after the package is disposed.
internal sealed record DocxContent(
    Body Body,
    DocxStyles Styles,
    DocxLists Lists,
    DocxHyperlinks Hyperlinks,
    DocxNotes Footnotes,
    DocxNotes Endnotes);

// A part's hyperlink addresses by relationship ID. Each part has its own
// relationships, so a footnote's links are looked up in the footnotes part.
internal sealed record DocxHyperlinks(IReadOnlyDictionary<string, string> UrlsById)
{
    public static readonly DocxHyperlinks None = new(new Dictionary<string, string>());
}

// The notes of one kind, by ID. Only referenced notes are written, so the
// separator notes Word adds for the line above the notes never are.
internal sealed record DocxNotes(IReadOnlyDictionary<long, FootnoteEndnoteType> NotesById, DocxHyperlinks Hyperlinks)
{
    public static readonly DocxNotes None = new(new Dictionary<long, FootnoteEndnoteType>(), DocxHyperlinks.None);
}
