using kisatsingen.OfficeConversion;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Xunit;

namespace kisatsingen.Tests.OfficeConversion;

public sealed class MarkdownTableTests
{
    private const int GenerousLimit = 1_000_000;

    private static readonly MarkdownPipeline PipeTables = new MarkdownPipelineBuilder().UsePipeTables().Build();

    private static string Write(int maxCharacters, params MarkdownTableCell[][] rows)
    {
        var output = new MarkdownOutput(maxCharacters);
        MarkdownTable.Write(rows, output);
        return output.ToString();
    }

    private static string Write(params MarkdownTableCell[][] rows) => Write(GenerousLimit, rows);

    private static MarkdownTableCell[] Row(params string[] texts) => [.. texts.Select(text => new MarkdownTableCell(text))];

    // Each row's cell texts, as a Markdown reader sees them.
    private static List<List<string>> ParseTable(string markdown)
    {
        var table = Assert.Single(Markdown.Parse(markdown, PipeTables).OfType<Table>());
        return [.. table.OfType<TableRow>().Select(row => row.OfType<TableCell>().Select(CellText).ToList())];
    }

    private static string CellText(TableCell cell) =>
        string.Concat(cell.Descendants<Markdig.Syntax.Inlines.LiteralInline>().Select(literal => literal.Content.ToString()));

    [Fact]
    public void The_first_row_is_the_header()
    {
        var markdown = Write(Row("Navn", "Rolle"), Row("Kari", "Rektor"));

        Assert.Equal("| Navn | Rolle |\n| --- | --- |\n| Kari | Rektor |\n", markdown);
    }

    [Fact]
    public void A_table_of_one_row_is_a_header_alone()
    {
        var markdown = Write(Row("Navn", "Rolle"));

        Assert.Equal("| Navn | Rolle |\n| --- | --- |\n", markdown);
    }

    [Fact]
    public void A_table_without_rows_writes_nothing()
    {
        var markdown = Write();

        Assert.Equal("", markdown);
    }

    [Fact]
    public void A_table_whose_rows_have_no_cells_writes_nothing()
    {
        var markdown = Write([], []);

        Assert.Equal("", markdown);
    }

    [Fact]
    public void A_shorter_row_is_padded_so_every_row_has_the_same_columns()
    {
        var markdown = Write(Row("Sted", "Dato", "Signatur"), Row("Tønsberg"));

        Assert.Equal([["Sted", "Dato", "Signatur"], ["Tønsberg", "", ""]], ParseTable(markdown));
    }

    [Fact]
    public void A_cell_spanning_columns_has_its_text_in_the_first_and_leaves_the_rest_empty()
    {
        var markdown = Write(
            Row("Fornavn", "Etternavn", "Telefon"),
            [new MarkdownTableCell("Foresatte", ColumnSpan: 2), new MarkdownTableCell("12345678")]);

        Assert.Equal([["Fornavn", "Etternavn", "Telefon"], ["Foresatte", "", "12345678"]], ParseTable(markdown));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_cell_spanning_no_columns_still_takes_one(int columnSpan)
    {
        var markdown = Write(Row("A", "B"), [new MarkdownTableCell("1", columnSpan), new MarkdownTableCell("2")]);

        Assert.Equal([["A", "B"], ["1", "2"]], ParseTable(markdown));
    }

    [Fact]
    public void A_pipe_in_a_cell_stays_text()
    {
        var markdown = Write(Row("Svar", "Merknad"), Row("Ja | Nei", "-"));

        Assert.Equal([["Svar", "Merknad"], ["Ja | Nei", "-"]], ParseTable(markdown));
    }

    // A backslash right before a pipe would cancel an escape that only added
    // one; however many backslashes come before it, the pipe adds no column.
    [Theory]
    [InlineData("||")]
    [InlineData(@"C:\Ja\|Nei")]
    [InlineData(@"C:\Ja\\|Nei")]
    [InlineData(@"\\\|")]
    [InlineData(@"slutter med \")]
    public void A_pipe_after_any_number_of_backslashes_adds_no_column(string text)
    {
        var markdown = Write(Row("Svar", "Merknad"), Row(text, "-"));

        Assert.All(ParseTable(markdown), row => Assert.Equal(2, row.Count));
    }

    [Fact]
    public void Inline_markdown_in_a_cell_is_kept_as_written()
    {
        var markdown = Write(Row("Felt"), Row(@"**Fornavn** og \# uten overskrift"));

        Assert.Equal("| Felt |\n| --- |\n| **Fornavn** og \\# uten overskrift |\n", markdown);
    }

    [Theory]
    [InlineData("Linje en\nLinje to")]
    [InlineData("Linje en\r\nLinje to")]
    [InlineData("Linje en\rLinje to")]
    public void A_line_break_in_a_cell_becomes_br_and_keeps_the_row_whole(string text)
    {
        var markdown = Write(Row("Samtykke"), Row(text));

        Assert.Equal("| Samtykke |\n| --- |\n| Linje en<br>Linje to |\n", markdown);
    }

    // A few bytes of XML can ask for this; the output's limit stops it
    // before the cells exist, rather than after memory is gone.
    [Fact]
    public void A_cell_spanning_more_columns_than_the_limit_allows_is_refused_while_writing()
    {
        Assert.Throws<MarkdownTooLongException>(() =>
            Write(maxCharacters: 10_000, Row("Overskrift"), [new MarkdownTableCell("Fyll", ColumnSpan: int.MaxValue)]));
    }

    [Fact]
    public void Output_up_to_the_limit_is_written()
    {
        var output = new MarkdownOutput(maxCharacters: 5);

        output.Append("12345");

        Assert.Equal("12345", output.ToString());
    }

    [Fact]
    public void Output_one_character_past_the_limit_is_refused()
    {
        var output = new MarkdownOutput(maxCharacters: 5);
        output.Append("1234");

        Assert.Throws<MarkdownTooLongException>(() => output.Append("56"));
    }
}
