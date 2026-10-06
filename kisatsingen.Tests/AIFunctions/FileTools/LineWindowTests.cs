using kisatsingen.AIFunctions.FileTools;
using kisatsingen.Services.KnowledgeFiles.Documents;
using Xunit;

namespace kisatsingen.Tests.AIFunctions.FileTools;

public sealed class LineWindowTests
{
    private const int NoTokenCap = 1_000_000;
    private const int NoLineCap = 1_000_000;

    private static LineWindowResult Read(string markdown, int start, int end, int maxTokens = NoTokenCap, int maxLineCharacters = NoLineCap) =>
        LineWindow.Read(DocumentLines.Split(markdown), DocumentOutline.Build(markdown), start, end, maxTokens, maxLineCharacters);

    [Fact]
    public void The_requested_lines_are_returned_numbered_like_cat_n()
    {
        var window = Read("en\nto\ntre\nfire", start: 2, end: 3);

        Assert.Equal("2\tto\n3\ttre", window.Content);
        Assert.Equal(2, window.FirstLine);
        Assert.Equal(3, window.LastLine);
        Assert.Null(window.ContinueFromLine);
    }

    [Fact]
    public void An_end_past_the_last_line_stops_at_the_last_line_without_a_continuation()
    {
        var window = Read("en\nto", start: 1, end: 50);

        Assert.Equal(2, window.LastLine);
        Assert.Null(window.ContinueFromLine);
    }

    [Fact]
    public void A_read_stops_at_the_token_cap_and_says_which_line_to_continue_from()
    {
        // Each numbered line is 31 characters plus a newline: two lines are
        // 21 estimated tokens, three are 32, over a cap of 30.
        var line = new string('x', 29);

        var window = Read(string.Join('\n', Enumerable.Repeat(line, 5)), start: 1, end: 5, maxTokens: 30);

        Assert.Equal(2, window.LastLine);
        Assert.Equal(3, window.ContinueFromLine);
    }

    [Fact]
    public void The_first_line_is_returned_even_when_it_alone_is_over_the_cap()
    {
        var window = Read(new string('x', 60) + "\nneste", start: 1, end: 2, maxTokens: 1);

        Assert.Equal(1, window.LastLine);
        Assert.Equal(2, window.ContinueFromLine);
    }

    [Fact]
    public void A_line_over_the_character_cap_is_cut_with_a_marker_saying_how_much_is_shown()
    {
        var window = Read(new string('a', 25), start: 1, end: 1, maxLineCharacters: 10);

        Assert.Equal("1\t" + new string('a', 10) + FileToolTexts.LineCutMarker(10, 25), window.Content);
    }

    [Fact]
    public void A_range_that_ends_inside_a_table_is_extended_to_the_end_of_the_table()
    {
        var window = Read("Tekst\n\n| a | b |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |\n\nEtter", start: 1, end: 4);

        Assert.Equal(6, window.LastLine);
        Assert.Null(window.ContinueFromLine);
    }

    [Fact]
    public void A_table_that_does_not_fit_under_the_cap_is_cut_with_a_continuation()
    {
        var rows = Enumerable.Range(1, 20).Select(row => $"| rad {row} | {new string('x', 30)} |");
        var markdown = string.Join('\n', ["| a | b |", "|---|---|", .. rows]);

        var window = Read(markdown, start: 1, end: 3, maxTokens: 50);

        Assert.True(window.LastLine is > 3 and < 22, $"Expected the read to run past line 3 into the table but stop before its end, got {window.LastLine}.");
        Assert.Equal(window.LastLine + 1, window.ContinueFromLine);
    }

    [Fact]
    public void A_cut_inside_an_extended_table_still_ends_the_range_at_the_tables_end()
    {
        var rows = Enumerable.Range(1, 20).Select(row => $"| rad {row} | {new string('x', 30)} |");
        var markdown = string.Join('\n', ["| a | b |", "|---|---|", .. rows]);

        var window = Read(markdown, start: 1, end: 3, maxTokens: 50);

        Assert.Equal(22, window.RangeEndLine);
    }
}
