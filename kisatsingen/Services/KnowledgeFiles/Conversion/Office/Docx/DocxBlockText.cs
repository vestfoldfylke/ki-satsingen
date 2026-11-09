using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

// The text of blocks where Markdown can only hold inline text: a table
// cell, or a note. One entry per paragraph, its lines separated by "\n".
//
// A nested table is flattened where it is, one entry per row with its cells
// separated by " | ", so its text stays in the cell it belongs to.
internal sealed class DocxBlockText(DocxParagraphReader reader, CancellationToken ct)
{
    private const string LineSeparator = "\n";
    private const string CellSeparator = " | ";

    public List<string> ParagraphsOf(OpenXmlElement container)
    {
        var paragraphs = new List<string>();
        AddChildren(container, paragraphs);
        return paragraphs;
    }

    public string CellTextOf(TableCell cell) => string.Join(LineSeparator, ParagraphsOf(cell));

    private void AddChildren(OpenXmlElement container, List<string> paragraphs)
    {
        foreach (var element in container.ChildElements)
        {
            AddBlock(element, paragraphs);
        }
    }

    private void AddBlock(OpenXmlElement element, List<string> paragraphs)
    {
        ct.ThrowIfCancellationRequested();

        if (DocxSkippedText.IsSkipped(element))
        {
            return;
        }

        switch (element)
        {
            case Paragraph paragraph:
                AddParagraph(paragraph, paragraphs);
                break;
            case Table table:
                AddTable(table, paragraphs);
                break;
            default:
                AddChildren(element, paragraphs);
                break;
        }
    }

    private void AddParagraph(Paragraph paragraph, List<string> paragraphs)
    {
        var content = reader.Read(paragraph, allowsEmphasis: true);
        foreach (var lines in DocxInlineMarkdown.ParagraphsOf(DocxInlineMarkdown.Write(content.Pieces)))
        {
            paragraphs.Add(string.Join(LineSeparator, lines.Select(DocxMarkdownEscape.BlockStart)));
        }

        foreach (var nested in content.NestedBlocks)
        {
            AddBlock(nested, paragraphs);
        }
    }

    // Empty cells are left out, since a flattened row has no columns to keep.
    private void AddTable(Table table, List<string> paragraphs)
    {
        foreach (var row in DocxTables.RowsOf(table, CellTextOf, ct))
        {
            var cellTexts = row
                .Select(cell => cell.Text.ReplaceLineEndings(" ").Trim())
                .Where(text => text.Length > 0)
                .ToList();

            if (cellTexts.Count > 0)
            {
                paragraphs.Add(string.Join(CellSeparator, cellTexts));
            }
        }
    }
}
