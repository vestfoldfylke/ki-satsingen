using DocumentFormat.OpenXml.Wordprocessing;
using kisatsingen.Services.WordDocuments;

namespace kisatsingen.Services.MeetingDocuments;

// Fills the template's agenda table (Saksliste): the header row is kept, and
// each item gets a copy of the template's first empty row, so the cells keep
// the template's widths and spacing.
internal static class AgendaTable
{
    public const int ColumnCount = 5;

    public static void Fill(Body body, IReadOnlyList<AgendaItemSpec> items)
    {
        var table = body.Elements<Table>().FirstOrDefault()
            ?? throw TemplateChanged("no agenda table");
        var rows = table.Elements<TableRow>().ToList();
        if (rows.Count < 2 || rows[1].Elements<TableCell>().Count() != ColumnCount)
        {
            throw TemplateChanged($"an agenda table without a header row and an empty row of {ColumnCount} cells");
        }

        var emptyRow = (TableRow)rows[1].CloneNode(deep: true);
        foreach (var row in rows.Skip(1))
        {
            row.Remove();
        }

        foreach (var item in items)
        {
            table.Append(CreateRow(emptyRow, item));
        }
    }

    private static TableRow CreateRow(TableRow emptyRow, AgendaItemSpec item)
    {
        var row = (TableRow)emptyRow.CloneNode(deep: true);
        string?[] cells = [item.Item, item.Description, item.Decision, item.Responsible, item.Deadline];

        foreach (var (cell, text) in row.Elements<TableCell>().Zip(cells))
        {
            if (text is not null && cell.Elements<Paragraph>().FirstOrDefault() is { } paragraph)
            {
                paragraph.Append(WordText.CreateRun(text));
            }
        }

        return row;
    }

    private static InvalidOperationException TemplateChanged(string what) =>
        new($"The meeting Word template has {what}. The template changed; update {nameof(AgendaTable)} to match it.");
}
