namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

internal enum DocxNoteKind
{
    Footnote,
    Endnote
}

internal readonly record struct DocxNoteReference(DocxNoteKind Kind, long Id);

// The notes referenced while writing, in the order first referenced, so
// their definitions after the body follow the reading order. Each note is
// listed once however often it is referenced.
internal sealed class DocxNoteReferences(DocxNotes footnotes, DocxNotes endnotes)
{
    private readonly List<DocxNoteReference> _referenced = [];
    private readonly HashSet<DocxNoteReference> _seen = [];

    public IReadOnlyList<DocxNoteReference> Referenced => _referenced;

    public DocxNotes NotesOf(DocxNoteKind kind) => kind == DocxNoteKind.Footnote
        ? footnotes
        : endnotes;

    // The GFM label to write at the reference, such as "[^1]", or null when
    // the document has no such note.
    public string? Reference(DocxNoteKind kind, long id)
    {
        if (!NotesOf(kind).NotesById.ContainsKey(id))
        {
            return null;
        }

        var reference = new DocxNoteReference(kind, id);
        if (_seen.Add(reference))
        {
            _referenced.Add(reference);
        }

        return $"[^{LabelOf(reference)}]";
    }

    // Footnotes and endnotes number from 1 each, so endnotes get a prefix.
    public static string LabelOf(DocxNoteReference reference) => reference.Kind == DocxNoteKind.Footnote
        ? $"{reference.Id}"
        : $"e{reference.Id}";
}
