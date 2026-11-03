using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

// How much text a document has, for noticing text a conversion lost without
// a sound: the characters in its text elements, outside what DocxSkippedText
// leaves out on purpose. Whitespace isn't counted, since the Markdown trims it.
internal static class DocxTextCount
{
    public static int Of(DocxContent content, CancellationToken ct) =>
        CountIn(content.Body, ct)
        + content.Footnotes.NotesById.Values.Sum(note => CountIn(note, ct))
        + content.Endnotes.NotesById.Values.Sum(note => CountIn(note, ct));

    public static int NonWhitespaceCharacters(string text) => text.Count(character => !char.IsWhiteSpace(character));

    // An explicit stack rather than recursion, since this walks whole parts.
    private static int CountIn(OpenXmlElement root, CancellationToken ct)
    {
        var characters = 0;
        var elements = new Stack<OpenXmlElement>([root]);

        while (elements.TryPop(out var element))
        {
            ct.ThrowIfCancellationRequested();

            if (DocxSkippedText.IsSkipped(element))
            {
                continue;
            }

            if (element is Text text)
            {
                characters += NonWhitespaceCharacters(text.Text);
                continue;
            }

            foreach (var child in element.ChildElements)
            {
                elements.Push(child);
            }
        }

        return characters;
    }
}

// Counts the document text a conversion wrote, to compare with DocxTextCount.
internal sealed class DocxTextTally
{
    public int Characters { get; private set; }

    public void Add(string text) => Characters += DocxTextCount.NonWhitespaceCharacters(text);
}
