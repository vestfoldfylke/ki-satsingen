using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace kisatsingen.Services.WordDocuments;

// Turns a block into the body elements that show it. Styling comes from the
// template's styles; only the simple template's tables carry formatting of
// their own, since its table style is a bare grid.
internal static class WordBlockFactory
{
    // tblLook's legacy bitmask, kept in step with its attributes: noVBand is
    // always on, firstRow only with a header row.
    private const int TableLookFirstRow = 0x0020;
    private const int TableLookNoVerticalBand = 0x0400;

    // The skill this follows uses a light blue header and soft grey borders.
    private const string TableHeaderFill = "D9E2EC";
    private const string TableBorderColor = "BFC9D4";
    private const uint TableBorderSize = 4;
    private const string CellMarginVertical = "80";
    private const string CellMarginHorizontal = "120";

    public static IEnumerable<OpenXmlElement> Create(DocumentBlockSpec block, WordNumbering numbering, int contentWidth, WordTableFormat tableFormat) => block.Kind switch
    {
        DocumentBlockKind.BulletList => CreateList(block.Items ?? [], numbering.BulletInstanceId),
        DocumentBlockKind.NumberedList => CreateList(block.Items ?? [], numbering.AddNumberedList()),
        DocumentBlockKind.Table => [CreateTable(block.HeaderRow, block.Rows ?? [], contentWidth, tableFormat)],
        DocumentBlockKind.PageBreak => [new Paragraph(new Run(new Break { Type = BreakValues.Page }))],
        { } kind => [CreateParagraph(WordStyles.For(kind), block.Text ?? "")],
        null => throw new InvalidOperationException($"A Word block has no kind, so it cannot be built. The spec reached {nameof(WordBlockFactory)} unvalidated; check it with {nameof(WordDocumentSpecValidator)} first.")
    };

    private static IEnumerable<OpenXmlElement> CreateList(IReadOnlyList<string> items, int instanceId) =>
        items.Select(item =>
        {
            var paragraph = CreateParagraph(WordStyles.ListParagraph, item);
            paragraph.ParagraphProperties!.Append(new NumberingProperties(
                new NumberingLevelReference { Val = 0 },
                new NumberingId { Val = instanceId }));
            return paragraph;
        });

    private static Paragraph CreateParagraph(string? styleId, string text, bool isBold = false)
    {
        var properties = new ParagraphProperties();
        if (styleId is not null)
        {
            properties.Append(new ParagraphStyleId { Val = styleId });
        }

        return new Paragraph(properties, WordText.CreateRun(text, isBold));
    }

    private static Table CreateTable(IReadOnlyList<string>? headerRow, IReadOnlyList<IReadOnlyList<string>> rows, int contentWidth, WordTableFormat format)
    {
        var columnCount = headerRow?.Count ?? rows[0].Count;
        var widths = ColumnWidths(columnCount, contentWidth);
        var isFormattedHere = !format.IsStyledByTemplate;

        var properties = new TableProperties(
            new TableStyle { Val = format.StyleId },
            new TableWidth { Width = contentWidth.ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa });
        if (isFormattedHere)
        {
            properties.Append(CreateBorders());
        }

        var hasHeaderRow = headerRow is not null;
        var lookBits = TableLookNoVerticalBand | (hasHeaderRow ? TableLookFirstRow : 0);
        properties.Append(new TableLook
        {
            Val = lookBits.ToString("X4", CultureInfo.InvariantCulture),
            FirstRow = hasHeaderRow,
            LastRow = false,
            FirstColumn = false,
            LastColumn = false,
            NoHorizontalBand = false,
            NoVerticalBand = true
        });

        var table = new Table(properties, new TableGrid(widths.Select(width => new GridColumn { Width = width.ToString(CultureInfo.InvariantCulture) })));

        if (headerRow is not null)
        {
            // Repeated at the top of every page the table runs over.
            table.Append(CreateRow(headerRow, widths, isHeader: true, isFormattedHere));
        }

        foreach (var row in rows)
        {
            table.Append(CreateRow(row, widths, isHeader: false, isFormattedHere));
        }

        return table;
    }

    // Equal columns; the last takes what division leaves over.
    private static int[] ColumnWidths(int columnCount, int contentWidth)
    {
        var width = contentWidth / columnCount;
        return [.. Enumerable.Repeat(width, columnCount - 1), contentWidth - width * (columnCount - 1)];
    }

    private static TableRow CreateRow(IReadOnlyList<string> cells, int[] widths, bool isHeader, bool isFormattedHere)
    {
        var row = new TableRow();
        if (isHeader)
        {
            row.Append(new TableRowProperties(new TableHeader()));
        }

        for (var i = 0; i < cells.Count; i++)
        {
            row.Append(CreateCell(cells[i], widths[i], isHeader, isFormattedHere));
        }

        return row;
    }

    // Shading, bold header and cell margins are the builder's only where the
    // template's table style has none.
    private static TableCell CreateCell(string text, int width, bool isHeader, bool isFormattedHere)
    {
        var hasOwnHeaderLook = isHeader && isFormattedHere;
        var properties = new TableCellProperties(new TableCellWidth { Width = width.ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa });
        if (hasOwnHeaderLook)
        {
            properties.Append(new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = TableHeaderFill });
        }

        if (isFormattedHere)
        {
            properties.Append(CreateCellMargins());
        }

        return new TableCell(properties, CreateParagraph(styleId: null, text, isBold: hasOwnHeaderLook));
    }

    private static TableCellMargin CreateCellMargins() => new(
        new TopMargin { Width = CellMarginVertical, Type = TableWidthUnitValues.Dxa },
        new LeftMargin { Width = CellMarginHorizontal, Type = TableWidthUnitValues.Dxa },
        new BottomMargin { Width = CellMarginVertical, Type = TableWidthUnitValues.Dxa },
        new RightMargin { Width = CellMarginHorizontal, Type = TableWidthUnitValues.Dxa });

    private static TableBorders CreateBorders() => new(
        new TopBorder { Val = BorderValues.Single, Size = TableBorderSize, Space = 0, Color = TableBorderColor },
        new LeftBorder { Val = BorderValues.Single, Size = TableBorderSize, Space = 0, Color = TableBorderColor },
        new BottomBorder { Val = BorderValues.Single, Size = TableBorderSize, Space = 0, Color = TableBorderColor },
        new RightBorder { Val = BorderValues.Single, Size = TableBorderSize, Space = 0, Color = TableBorderColor },
        new InsideHorizontalBorder { Val = BorderValues.Single, Size = TableBorderSize, Space = 0, Color = TableBorderColor },
        new InsideVerticalBorder { Val = BorderValues.Single, Size = TableBorderSize, Space = 0, Color = TableBorderColor });
}
