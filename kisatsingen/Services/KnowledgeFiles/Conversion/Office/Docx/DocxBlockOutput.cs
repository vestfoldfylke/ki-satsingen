namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

// Writes Markdown blocks with a blank line between them. List items in a
// row are kept on consecutive lines, so they read as one list; a paragraph
// continuing an item has blank lines around it, as Markdown needs.
internal sealed class DocxBlockOutput(MarkdownOutput output)
{
    private const string BlankLine = "\n\n";
    private const string NewLine = "\n";

    private BlockKind _previous = BlockKind.None;

    public bool IsInList => _previous is BlockKind.ListItem or BlockKind.ListContinuation;

    public void Paragraph(string markdown) => Append(markdown, BlockKind.Paragraph);

    public void ListItem(string markdown) => Append(markdown, BlockKind.ListItem);

    public void ListContinuation(string markdown) => Append(markdown, BlockKind.ListContinuation);

    // A table with no text in any cell is left out.
    public void Table(IReadOnlyList<IReadOnlyList<MarkdownTableCell>> rows)
    {
        if (!rows.Any(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell.Text))))
        {
            return;
        }

        WriteSeparator(BlockKind.Table);
        MarkdownTable.Write(rows, output);
        _previous = BlockKind.Table;
    }

    private void Append(string markdown, BlockKind kind)
    {
        WriteSeparator(kind);
        output.Append(markdown);
        _previous = kind;
    }

    // A table ends its own last line, so one newline after it makes the blank line.
    private void WriteSeparator(BlockKind next)
    {
        var separator = _previous switch
        {
            BlockKind.None => "",
            BlockKind.Table => NewLine,
            BlockKind.ListItem when next == BlockKind.ListItem => NewLine,
            _ => BlankLine
        };

        output.Append(separator);
    }

    private enum BlockKind
    {
        None,
        Paragraph,
        ListItem,
        ListContinuation,
        Table
    }
}
