using Xunit;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.ConvertDocx;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.TestDocx;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx;

public sealed class DocxTablesTests
{
    private static string Table(params string[] rows) => $"<w:tbl><w:tblGrid/>{string.Concat(rows)}</w:tbl>";

    private static string Row(params string[] cells) => $"<w:tr>{string.Concat(cells)}</w:tr>";

    private static string Cell(string content, string properties = "") =>
        $"<w:tc>{(properties.Length > 0 ? $"<w:tcPr>{properties}</w:tcPr>" : "")}{content}</w:tc>";

    private static string TextCell(string text, string properties = "") => Cell(Paragraph(text), properties);

    [Fact]
    public void A_table_becomes_a_markdown_table_with_its_first_row_as_header()
    {
        var docx = WithBody(Table(Row(TextCell("Navn"), TextCell("Rolle")), Row(TextCell("Kari"), TextCell("Leder"))));

        var markdown = ToMarkdown(docx);

        Assert.Equal("| Navn | Rolle |\n| --- | --- |\n| Kari | Leder |", markdown);
    }

    [Fact]
    public void A_table_is_separated_from_the_paragraphs_around_it_by_blank_lines()
    {
        var docx = WithBody(Paragraph("Før") + Table(Row(TextCell("Celle"))) + Paragraph("Etter"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("Før\n\n| Celle |\n| --- |\n\nEtter", markdown);
    }

    [Fact]
    public void A_cell_spanning_columns_keeps_every_row_the_same_width()
    {
        var docx = WithBody(Table(
            Row(TextCell("Overskrift", "<w:gridSpan w:val=\"2\"/>")),
            Row(TextCell("A"), TextCell("B"))));

        var markdown = ToMarkdown(docx);

        Assert.Equal("| Overskrift |  |\n| --- | --- |\n| A | B |", markdown);
    }

    // Some tools repeat the merged text in the covered cells.
    [Fact]
    public void A_vertically_merged_cell_has_its_text_in_the_first_row_only()
    {
        var docx = WithBody(Table(
            Row(TextCell("Gruppe", "<w:vMerge w:val=\"restart\"/>"), TextCell("En")),
            Row(TextCell("Gruppe", "<w:vMerge/>"), TextCell("To"))));

        var markdown = ToMarkdown(docx);

        Assert.Equal("| Gruppe | En |\n| --- | --- |\n|  | To |", markdown);
    }

    [Fact]
    public void A_cell_s_paragraphs_and_line_breaks_become_br()
    {
        var cell = Cell($"{Paragraph("Første")}<w:p>{Run("Andre")}<w:r><w:br/></w:r>{Run("Tredje")}</w:p>");
        var docx = WithBody(Table(Row(cell)));

        var markdown = ToMarkdown(docx);

        Assert.Equal("| Første<br>Andre<br>Tredje |\n| --- |", markdown);
    }

    // Undoc drops a repeated paragraph in a cell, and loses data with it.
    [Fact]
    public void Repeated_paragraphs_in_a_cell_are_all_kept()
    {
        var docx = WithBody(Table(Row(Cell(Paragraph("Ja") + Paragraph("Ja")))));

        var markdown = ToMarkdown(docx);

        Assert.Equal("| Ja<br>Ja |\n| --- |", markdown);
    }

    // Undoc writes nested tables after the outer table, and DocSharp loses their text.
    [Fact]
    public void A_nested_table_s_text_stays_in_its_cell()
    {
        var nested = Table(Row(TextCell("Indre A"), TextCell("Indre B")), Row(TextCell("Indre C"), TextCell("Indre D")));
        var docx = WithBody(Table(Row(Cell(Paragraph("Ytre") + nested + "<w:p/>"), TextCell("Nabo"))));

        var markdown = ToMarkdown(docx);

        Assert.Equal("| Ytre<br>Indre A \\| Indre B<br>Indre C \\| Indre D | Nabo |\n| --- | --- |", markdown);
    }

    [Fact]
    public void A_bold_cell_keeps_its_bold()
    {
        var docx = WithBody(Table(Row(Cell($"<w:p>{Run("Fornavn", "<w:b/>")}</w:p>"))));

        var markdown = ToMarkdown(docx);

        Assert.Equal("| **Fornavn** |\n| --- |", markdown);
    }

    [Fact]
    public void A_table_without_text_is_left_out()
    {
        var docx = WithBody(Paragraph("Før") + Table(Row(Cell("<w:p/>"), Cell("<w:p/>"))) + Paragraph("Etter"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("Før\n\nEtter", markdown);
    }

    [Fact]
    public void A_deleted_row_is_left_out()
    {
        var deletedRow = $"<w:tr><w:trPr><w:del w:id=\"1\" w:author=\"A\"/></w:trPr>{TextCell("Slettet")}</w:tr>";
        var docx = WithBody(Table(Row(TextCell("Topp")), deletedRow, Row(TextCell("Bunn"))));

        var markdown = ToMarkdown(docx);

        Assert.Equal("| Topp |\n| --- |\n| Bunn |", markdown);
    }

    [Fact]
    public void A_row_in_a_content_control_is_kept()
    {
        var docx = WithBody(Table(Row(TextCell("Topp")), $"<w:sdt><w:sdtContent>{Row(TextCell("Inni"))}</w:sdtContent></w:sdt>"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("| Topp |\n| --- |\n| Inni |", markdown);
    }
}
