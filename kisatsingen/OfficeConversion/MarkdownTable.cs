using System.Text;

namespace kisatsingen.OfficeConversion;

// Writes rows as a GitHub-flavoured Markdown table. The first row is the
// header, since a Markdown table can't be written without one. Every row is
// padded to the widest, so a merged cell never shifts the columns after it.
//
// Writes whole lines, ending with a newline. The caller separates the table
// from the blocks around it with a blank line.
internal static class MarkdownTable
{
    private const string EmptyCell = "  |";

    public static void Write(IReadOnlyList<IReadOnlyList<MarkdownTableCell>> rows, MarkdownOutput output)
    {
        // A long, since every span comes from the document and they can add up past int.
        var columnCount = rows.Count == 0 ? 0 : rows.Max(row => row.Sum(cell => (long)SpanOf(cell)));
        if (columnCount == 0)
        {
            return;
        }

        WriteRow(rows[0], columnCount, output);
        WriteSeparator(columnCount, output);
        foreach (var row in rows.Skip(1))
        {
            WriteRow(row, columnCount, output);
        }
    }

    private static void WriteRow(IReadOnlyList<MarkdownTableCell> row, long columnCount, MarkdownOutput output)
    {
        output.Append("|");
        long columnsWritten = 0;
        foreach (var cell in row)
        {
            var span = SpanOf(cell);
            var coveredColumns = span - 1;

            output.Append($" {EscapeCellText(cell.Text)} |");
            for (var column = 0; column < coveredColumns; column++)
            {
                output.Append(EmptyCell);
            }

            columnsWritten += span;
        }

        while (columnsWritten < columnCount)
        {
            output.Append(EmptyCell);
            columnsWritten++;
        }

        output.Append("\n");
    }

    private static void WriteSeparator(long columnCount, MarkdownOutput output)
    {
        output.Append("|");
        for (long column = 0; column < columnCount; column++)
        {
            output.Append(" --- |");
        }

        output.Append("\n");
    }

    // A broken document can say a cell spans zero or fewer columns; it still takes one.
    private static int SpanOf(MarkdownTableCell cell) => Math.Max(cell.ColumnSpan, 1);

    // A line break would end the row, so it becomes <br>.
    private static string EscapeCellText(string text) =>
        EscapePipes(text.Trim()).ReplaceLineEndings("<br>");

    // A pipe ends the cell unless an odd number of backslashes comes before
    // it, so one is added where the count is even. Other backslashes are left
    // alone: the text is inline Markdown, and its escapes such as \# must work.
    private static string EscapePipes(string text)
    {
        var escaped = new StringBuilder(text.Length);
        var precedingBackslashes = 0;
        foreach (var character in text)
        {
            if (character == '|' && precedingBackslashes % 2 == 0)
            {
                escaped.Append('\\');
            }

            escaped.Append(character);
            precedingBackslashes = character == '\\' ? precedingBackslashes + 1 : 0;
        }

        return escaped.ToString();
    }
}

// Text is inline Markdown, such as "**Fornavn**". A cell that spans columns,
// like Word's gridSpan, covers ColumnSpan grid columns: its text in the
// first, the rest left empty.
internal sealed record MarkdownTableCell(string Text, int ColumnSpan = 1);
