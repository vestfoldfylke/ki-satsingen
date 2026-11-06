using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using static kisatsingen.Services.KnowledgeFiles.Conversion.Office.OpenXmlValues;

namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

// A Word table as rows of MarkdownTableCell, keeping every row on the same
// grid: a cell spanning columns keeps its span, and the cells a merge covers
// are left empty.
internal static class DocxTables
{
    private const string RestartMergeValue = "restart";

    public static List<IReadOnlyList<MarkdownTableCell>> RowsOf(Table table, Func<TableCell, string> textOf, CancellationToken ct)
    {
        var rows = new List<IReadOnlyList<MarkdownTableCell>>();
        foreach (var row in ChildrenThroughWrappers<TableRow>(table))
        {
            ct.ThrowIfCancellationRequested();

            // A tracked deletion of the whole row, read as accepted.
            if (row.TableRowProperties?.GetFirstChild<Deleted>() is not null)
            {
                continue;
            }

            rows.Add(CellsOf(row, textOf));
        }

        return rows;
    }

    private static List<MarkdownTableCell> CellsOf(TableRow row, Func<TableCell, string> textOf)
    {
        var cells = new List<MarkdownTableCell>();

        // A row can start some grid columns in, which an empty cell keeps in place.
        if (ValueOf(row.TableRowProperties?.GetFirstChild<GridBefore>()?.Val) is > 0 and var skippedColumns)
        {
            cells.Add(new MarkdownTableCell("", skippedColumns));
        }

        foreach (var cell in ChildrenThroughWrappers<TableCell>(row))
        {
            var properties = cell.TableCellProperties;
            var text = IsCoveredByMerge(properties)
                ? ""
                : textOf(cell);

            cells.Add(new MarkdownTableCell(text, ValueOf(properties?.GridSpan?.Val) ?? 1));
        }

        return cells;
    }

    // A merge's text is in the cell that starts it (val="restart"); a merge
    // element without that value continues the one before.
    private static bool IsCoveredByMerge(TableCellProperties? properties) =>
        properties?.VerticalMerge is { } verticalMerge && verticalMerge.Val?.InnerText != RestartMergeValue
        || properties?.HorizontalMerge is { } horizontalMerge && horizontalMerge.Val?.InnerText != RestartMergeValue;

    // Rows and cells can be wrapped in content controls or custom XML, so
    // this looks through other elements, but never into a T, where a nested
    // table's rows and cells are.
    private static IEnumerable<T> ChildrenThroughWrappers<T>(OpenXmlElement parent) where T : OpenXmlElement
    {
        foreach (var child in parent.ChildElements)
        {
            if (DocxSkippedText.IsSkipped(child))
            {
                continue;
            }

            if (child is T match)
            {
                yield return match;
                continue;
            }

            foreach (var nested in ChildrenThroughWrappers<T>(child))
            {
                yield return nested;
            }
        }
    }
}
