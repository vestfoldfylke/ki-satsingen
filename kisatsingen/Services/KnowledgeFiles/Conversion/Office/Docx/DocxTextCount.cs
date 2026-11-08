using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using M = DocumentFormat.OpenXml.Math;

namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

// How much text a document's body has, for noticing text a conversion lost
// without a sound: the characters in its text elements, equations' included,
// outside what DocxSkippedText leaves out on purpose, deleted table rows and
// the cells a merge covers. Whitespace isn't counted, since the Markdown trims it.
//
// Notes aren't counted: a note nothing references is left out on purpose,
// and DocxBodyWriter.Write counts only the body's text too.
internal static class DocxTextCount
{
    public static int Of(DocxContent content, CancellationToken ct) => CountIn(content.Body, ct);

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

            if (element is TableRow row && DocxTables.IsDeleted(row))
            {
                continue;
            }

            if (element is TableCell cell && DocxTables.IsCoveredByMerge(cell.TableCellProperties))
            {
                continue;
            }

            if (element is (Text or M.Text) and OpenXmlLeafTextElement text)
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

// Counts the document text a conversion reads, to compare with DocxTextCount.
//
// Every character it counts is written to the Markdown, so once the count
// passes maxCharacters the Markdown would too. It stops the conversion then,
// while reading, before the text is copied into strings: one paragraph of 40
// million characters took about 476 MB before MarkdownOutput refused it.
internal sealed class DocxTextTally(int maxCharacters)
{
    public int Characters { get; private set; }

    public void Add(string text)
    {
        Characters += DocxTextCount.NonWhitespaceCharacters(text);
        if (Characters > maxCharacters)
        {
            throw new MarkdownTooLongException(maxCharacters);
        }
    }
}
