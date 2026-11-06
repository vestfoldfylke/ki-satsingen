namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

// Writes the notes the document referenced as GFM footnote definitions,
// "[^1]: text", after the body. A note's further paragraphs and lines are
// indented, which is how a definition continues.
internal static class DocxNotesWriter
{
    private const string Indent = "    ";
    private const string HardLineBreak = "  \n";
    private const string ParagraphBreak = "\n\n";

    public static void Write(DocxNoteReferences references, Func<DocxNoteKind, DocxBlockText> blockTextOf, DocxBlockOutput blocks)
    {
        // A note can reference another note, which adds to the list while it
        // is walked, so it is walked by index rather than with foreach.
        for (var index = 0; index < references.Referenced.Count; index++)
        {
            var reference = references.Referenced[index];
            var note = references.NotesOf(reference.Kind).NotesById[reference.Id];
            var paragraphs = blockTextOf(reference.Kind).ParagraphsOf(note);
            if (paragraphs.Count == 0)
            {
                continue;
            }

            var text = string.Join(
                ParagraphBreak + Indent,
                paragraphs.Select(paragraph => paragraph.Replace("\n", HardLineBreak + Indent)));

            blocks.Paragraph($"[^{DocxNoteReferences.LabelOf(reference)}]: {text}");
        }
    }
}
